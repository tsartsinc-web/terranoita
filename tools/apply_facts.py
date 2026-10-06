"""Apply facts read from the player's data.wak (tncli facts) to the design sheets.

  dotnet run --project src/Terranoita.Cli -- facts "<Noita folder>" design/sheets/enemies.json build/noita_facts.json
  python tools/apply_facts.py build/noita_facts.json [--stage 1a]

For each enemy in scope it fills and verifies: noita_entity, sprite, hitbox, noita_hp (data.wak wins over the wiki;
differences are printed), and for its attacks/projectiles the timings, ranges, counts and projectile numbers.
A cell is only marked verified when its value came from data.wak. Anything it cannot match is printed and left
unverified for a person (or agent) to resolve by reading the files with `tncli wak-cat`.

Facts written by the newer tncli also carry each entity's behaviour components ("components"; see
src/Terranoita.Core/Noita/EntityDump.cs) and Noita's component documentation ("_component_docs"). From those it also
fills: projectile effects (see EFFECT_RULES), creatures that do not move at all (no movement component of any kind),
attacks of creatures that have none, and the real entity file of wiki-seeded ids ("found_by").
"""
import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STAGES = ["1a", "1b", "1c"]
PIXEL_SCALE = 3.0      # 1 Noita pixel = 3 Terraria world pixels (author: Noita enemies at a normal size next to the player)
TILE = 16.0
FPS = 60.0


def load(d, name):
    with open(os.path.join(d, name + ".json"), encoding="utf-8") as f:
        return json.load(f)


def save(d, s):
    with open(os.path.join(d, s["sheet"] + ".json"), "w", encoding="utf-8") as f:
        json.dump(s, f, ensure_ascii=False, indent=1)
        f.write("\n")


def verify(row, col, value):
    row[col] = value
    row.setdefault("_unverified", {}).pop(col, None)


def filled(v):
    return v is not None and v != ""


def basename(p):
    return os.path.splitext(os.path.basename(p or ""))[0].lower()


def tokens(s):
    return set(t for t in re.split(r"[^a-z0-9]+", s.lower()) if t and t not in ("shot", "projectile", "weak"))


# ---- behaviour components (facts from the newer tncli) -------------------------------------------------------------

# components that move a creature some other way than CharacterPlatformingComponent
MOVERS = {"WormComponent", "WormAIComponent", "PhysicsAIComponent", "AdvancedFishAIComponent", "FishAIComponent",
          "IKLimbWalkerComponent", "IKLimbsAnimatorComponent", "LimbBossComponent", "CrawlerAnimalComponent",
          "BossDragonComponent", "LevitationComponent", "TeleportComponent", "VelocityComponent",
          "SimplePhysicsComponent", "PhysicsBodyComponent", "CharacterPlatformingComponent"}
# components that hurt or affect things by themselves (auras, explosions, scripted attacks)
ATTACKERS = {"AreaDamageComponent", "GameAreaEffectComponent", "ExplodeOnDamageComponent", "ExplosionComponent",
             "DamageNearbyEntitiesComponent", "AIAttackComponent", "IKLimbAttackerComponent", "LuaComponent",
             "ElectricChargeComponent", "MagicConvertMaterialComponent", "CellEaterComponent", "HomingComponent"}

# Noita game effect (GameEffectComponent effect=...) -> projectiles.effect
GAME_EFFECTS = {
    "ON_FIRE": "fire", "BURNING": "fire", "FROZEN": "ice", "ELECTROCUTION": "electric", "POISON": "poison",
    "RADIOACTIVE": "poison", "POLYMORPH": "polymorph", "POLYMORPH_RANDOM": "polymorph", "POLYMORPH_UNSTABLE": "polymorph",
    "POLYMORPH_CESSATION": "polymorph", "TELEPORTATION": "teleport", "TELEPORTITIS": "teleport",
    "UNSTABLE_TELEPORTATION": "teleport", "BLINDNESS": "blind", "REGENERATION": "heal", "NO_WAND_EDITING": "neutralize",
    "MANA_REGENERATION": "heal",
}
# material words (create_cell_material, emitted_material_name, ...) -> projectiles.effect, checked in this order
MATERIAL_WORDS = [("polymorph", "polymorph"), ("teleport", "teleport"), ("acid", "acid"), ("radioactive", "poison"),
                  ("poison", "poison"), ("lava", "fire"), ("fire", "fire"), ("spark_electric", "electric"),
                  ("ice", "ice"), ("freeze", "ice"), ("blood_cold", "ice"), ("glue", "glue"), ("slime", "glue"),
                  ("magic_liquid_hp_regeneration", "heal"), ("healing", "heal")]
DAMAGE_TYPES = [("fire", "fire"), ("ice", "ice"), ("electricity", "electric"), ("radioactive", "poison"),
                ("poison", "poison"), ("healing", "heal")]


def components(f, name=None):
    for c in f.get("components") or []:
        if name is None or c["component"] == name:
            yield c


def all_nodes(c):
    yield c
    for ch in c.get("children") or []:
        yield from all_nodes(ch)


def num(v, default=0.0):
    try:
        return float(v)
    except (TypeError, ValueError):
        return default


def derive_effect(pf, file):
    """(effect, evidence) for a projectile from its components, or (None, what was seen) when no rule decides."""
    comps = list(components(pf))
    if not comps:
        return None, "no components in the facts (old tncli)"
    for c in comps:
        if c["component"] in ("GameEffectComponent", "HitEffectComponent"):
            eff = (c["attrs"].get("effect") or c["attrs"].get("value_string") or "").upper()
            if eff in GAME_EFFECTS:
                return GAME_EFFECTS[eff], "%s: %s effect %s" % (file, c["component"], eff)
        if c["component"] == "TeleportProjectileComponent":
            return "teleport", "%s: TeleportProjectileComponent" % file
    materials = []
    for c in comps:
        for n in all_nodes(c):
            for k, v in n["attrs"].items():
                if "material" in k and isinstance(v, str) and v and not v.isdigit():
                    materials.append(v.lower())
    for word, eff in MATERIAL_WORDS:
        for m in materials:
            if word in m:
                return eff, "%s: material %s" % (file, m)
    for c in components(pf, "ProjectileComponent"):
        dbt = next((ch for ch in c.get("children") or [] if ch["component"] == "damage_by_type"), None)
        if dbt:
            for k, eff in DAMAGE_TYPES:
                if num(dbt["attrs"].get(k)) > 0:
                    return eff, "%s: damage_by_type %s" % (file, k)
        if num(c["attrs"].get("damage")) < 0:
            return "heal", "%s: negative damage (heals)" % file
    radius = max([num(n["attrs"].get("explosion_radius")) for c in comps for n in all_nodes(c)] + [0])
    if radius > 0 and any(c["attrs"].get(k) == "1" for c in components(pf, "ProjectileComponent")
                          for k in ("on_death_explode", "on_lifetime_out_explode", "on_collision_die")):
        return "explosive", "%s: explodes, radius %g" % (file, radius)
    seen = sorted(set(c["component"] for c in comps) - {"SpriteComponent", "AudioComponent", "VelocityComponent"})
    if seen == ["ProjectileComponent"] or not seen:
        return "none", "%s: plain damage, no effect, material or damage_by_type" % file
    return None, "components %s, materials %s" % (", ".join(seen), ", ".join(sorted(set(materials))) or "none")


def is_static(f):
    """True when the creature has no movement component of any kind (crystals, turrets, traps, nests)."""
    if f.get("movement") or not f.get("components"):
        return False
    return not any(c["component"] in MOVERS or c["component"] == "AnimalAIComponent" for c in components(f))


def spawned_files(f):
    """Entity files a creature's scripts and components name: what it summons or releases."""
    out = []
    for files in (f.get("script_entities") or {}).values():
        out += files or []
    for c in components(f):
        for n in all_nodes(c):
            out += [v for v in n["attrs"].values() if isinstance(v, str) and v.startswith("data/entities/animals/") and v.endswith(".xml")]
    return list(dict.fromkeys(out))


def has_own_attacks(f):
    return bool(f.get("ranged")) or bool(f.get("dash")) or any(c["component"] in ATTACKERS for c in components(f))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("facts")
    ap.add_argument("--stage", choices=STAGES, default="1c", help="apply to rows up to this stage (default: all)")
    ap.add_argument("--sheets", default=os.path.join(ROOT, "design", "sheets"))
    ap.add_argument("--sounds", default=os.path.join(ROOT, "build", "noita_sounds.txt"),
                    help="FMOD event list from the player's Noita (Terranoita.exe --list-noita-sounds)")
    args = ap.parse_args()
    sounds = set()
    if os.path.exists(args.sounds):
        sounds = set(l.strip()[len("event:/"):] for l in open(args.sounds, encoding="utf-8") if l.startswith("event:/"))

    def first_sound(roots, names):
        for r in roots:
            for n in names:
                if r + "/" + n in sounds:
                    return r + "/" + n
        return "none"
    limit = STAGES.index(args.stage)

    facts = json.load(open(args.facts, encoding="utf-8"))
    enemies, attacks, projectiles = (load(args.sheets, n) for n in ("enemies", "attacks", "projectiles"))
    attack_by_id = {a["id"]: a for a in attacks["rows"]}
    proj_by_id = {p["id"]: p for p in projectiles["rows"]}
    notes = []
    claimed = {}    # projectile id -> (noita_file, who set it)

    def own_projectile_row(shared, entity_file, enemy_id, attack):
        """A copy of a shared projectile row for the enemy whose data.wak file differs; the attack is repointed."""
        same = [x for x in proj_by_id.values() if claimed.get(x["id"], (x.get("noita_file"),))[0] == entity_file]
        if same:
            row, pid = same[0], same[0]["id"]
        else:
            pid = basename(entity_file)
            row = proj_by_id.get(pid)
            if row is not None:
                pid = "%s_%s" % (enemy_id, basename(entity_file))
                row = proj_by_id.get(pid)
        if row is None:
            row = {k: v for k, v in shared.items() if not k.startswith("_")}
            row.update(id=pid, name_en=shared["name_en"] + " (" + basename(entity_file) + ")", used_by=[],
                       noita_file=None, sprite=None, speed=None, gravity=None, lifetime_frames=None,
                       explosion_radius=None, audio=None, explosion_sound=None, stage=attack["stage"])
            row["_unverified"] = {"effect": "copied from %s; confirm in %s" % (shared["id"], entity_file)}
            projectiles["rows"].append(row)
            proj_by_id[pid] = row
        if enemy_id in shared.get("used_by", []):
            shared["used_by"].remove(enemy_id)
        if enemy_id not in row["used_by"]:
            row["used_by"].append(enemy_id)
        if STAGES.index(attack["stage"]) < STAGES.index(row["stage"]):
            row["stage"] = attack["stage"]
        attack["projectile"] = pid
        claimed[pid] = (entity_file, enemy_id)
        return row

    for e in enemies["rows"]:
        if STAGES.index(e["stage"]) > limit:
            continue
        f = facts.get(e["id"])
        if not f or "error" in f:
            notes.append("%s: %s" % (e["id"], f.get("error") if f else "no facts"))
            continue
        verify(e, "noita_entity", f["entity"])
        how = f.get("found_by")
        if how and how not in ("guess", "file_name"):
            notes.append("%s: entity found by %s -> %s (other matches: %s); check it is the same creature" % (
                e["id"], how, f["entity"], ", ".join(f.get("candidates") or []) or "none"))
        if how and "id" in e.get("_unverified", {}):
            e.setdefault("_sources", {})["id"] = "sheet id kept; Noita file %s (found by %s)" % (f["entity"], how)
            e["_unverified"].pop("id")
        if f.get("sprite") and f.get("sprite_image_exists"):
            verify(e, "sprite", f["sprite"])
        else:
            notes.append("%s: sprite %r not usable (image %r)" % (e["id"], f.get("sprite"), f.get("sprite_image")))
        if f.get("hitbox_noita_px") and min(f["hitbox_noita_px"]) > 0:
            verify(e, "hitbox", [int(round(v * PIXEL_SCALE)) for v in f["hitbox_noita_px"]])
        if f.get("display_hp") is not None:
            if e.get("noita_hp") is not None and abs(e["noita_hp"] - f["display_hp"]) > 0.01:
                notes.append("%s: hp wiki %s, data.wak %s (using data.wak)" % (e["id"], e["noita_hp"], f["display_hp"]))
            verify(e, "noita_hp", f["display_hp"])
        if f.get("name_key"):
            verify(e, "name_key", f["name_key"])
        m = f.get("movement")
        if m:
            px = lambda v: round((v or 0.0) * PIXEL_SCALE / FPS, 3)  # Noita px/s -> Terraria px/frame
            verify(e, "walks", m["can_walk"])
            verify(e, "flies", m["can_fly"])
            verify(e, "jumps", m["can_jump"])
            verify(e, "run_speed", px(m["run_velocity"]))
            verify(e, "fly_speed", px(m["fly_velocity_x"]))
            verify(e, "fly_up_speed", px(m["fly_speed_max_up"]))
            verify(e, "accel", m["accel_x"])
            verify(e, "gravity", round(m["pixel_gravity"] * PIXEL_SCALE / (FPS * FPS), 4))
            verify(e, "jump_speed", px(m["jump_speed"]))
            # 50 px: AnimalAIComponent's default when the entity does not set it
            verify(e, "sight_tiles", round((m["detection_range_px"] or 50.0) * PIXEL_SCALE / TILE, 1))
        elif is_static(f):
            for col in ("walks", "flies", "jumps"):
                verify(e, col, False)
            for col in ("run_speed", "fly_speed", "fly_up_speed", "accel", "gravity", "jump_speed"):
                verify(e, col, 0.0)
            # it notices the player as far as its own attacks reach (or AnimalAIComponent's 50 px default)
            reach = [r.get("max_distance_px") or 0 for r in f.get("ranged") or []]
            verify(e, "sight_tiles", round((max(reach) if reach and max(reach) > 0 else 50.0) * PIXEL_SCALE / TILE, 1))
            e.setdefault("_sources", {})["walks"] = "no movement component of any kind in %s: does not move" % f["entity"]
        else:
            movers = sorted(set(c["component"] for c in components(f) if c["component"] in MOVERS))
            notes.append("%s: moves without CharacterPlatformingComponent (%s); fill movement from its components" % (
                e["id"], ", ".join(movers) or "no components in facts: rerun the newer tncli facts"))
        # most specific folder first (animals/zombie before animals/generic before animals)
        roots = [r for r in (f.get("audio_roots") or []) if any(x.startswith(r + "/") for x in sounds)]
        roots.sort(key=lambda r: (r + "/death" not in sounds, r.endswith("/generic") or "/" not in r, -len(r)))
        if sounds:
            verify(e, "audio", roots[0] if roots else "none")
        dm = f.get("damage_multipliers") or {}
        if dm and e.get("dmg_mult"):
            for k in e["dmg_mult"]:
                if k in dm:
                    e["dmg_mult"][k] = dm[k]
            if all(k in dm for k in e["dmg_mult"]):
                e["_unverified"].pop("dmg_mult", None)

        if not e["attacks"] and "attacks" in e.get("_unverified", {}) and f.get("components") and not has_own_attacks(f):
            e["_unverified"].pop("attacks")
            e.setdefault("_sources", {})["attacks"] = "%s: no ranged, dash, aura, explosion or script attack" % f["entity"]

        # summons: the creatures its spawn scripts or components name
        by_file = {x.get("noita_entity"): x["id"] for x in enemies["rows"] if x.get("noita_entity")}
        for aid in e["attacks"]:
            a = attack_by_id[aid]
            if a["kind"] == "summon" and "summons" in a.get("_unverified", {}) and (f.get("components") or f.get("script_entities")):
                ids = [by_file[x] for x in spawned_files(f) if by_file.get(x) not in (None, e["id"])]
                if ids:
                    if a.get("summons") and sorted(a["summons"]) != sorted(ids):
                        notes.append("%s: summons %s (sheet guessed %s)" % (aid, ids, a["summons"]))
                    verify(a, "summons", ids)
                else:
                    notes.append("%s: no creature named in its scripts/components %s" % (aid, spawned_files(f)))

        # melee timing
        for aid in e["attacks"]:
            a = attack_by_id[aid]
            if a["kind"] == "melee" and f.get("melee_frames_between"):
                verify(a, "cooldown_frames", int(f["melee_frames_between"]))
                if f.get("melee_max_distance_px"):
                    verify(a, "range_tiles", round(f["melee_max_distance_px"] * PIXEL_SCALE / TILE, 2))
                if sounds:
                    verify(a, "sound", first_sound(roots, ["attack_melee", "_voc_attack", "voc_attack"]))
            elif a["kind"] == "lunge":
                dash = f.get("dash")
                if not dash:
                    notes.append("%s: sheet has %s but data.wak has no dash attack" % (e["id"], aid))
                    continue
                verify(a, "cooldown_frames", int(dash["frames_between"]))
                verify(a, "range_tiles", round(dash["distance_px"] * PIXEL_SCALE / TILE, 2))
                verify(a, "lunge_speed", round(dash["speed"] * PIXEL_SCALE / FPS, 3))
                if sounds:
                    verify(a, "sound", first_sound(roots, ["attack_dash", "voc_attack", "_voc_attack"]))
                shown = round(dash["damage"] * 25.0, 2)
                if a["damage"].get("melee") != shown:
                    notes.append("%s: dash damage wiki %s, data.wak %s (using data.wak)" % (aid, a["damage"].get("melee"), shown))
                verify(a, "damage", {"melee": shown})

        # ranged: match sheet projectile attacks to data.wak ranged attacks
        ranged = list(f.get("ranged") or [])
        proj_attacks = [attack_by_id[a] for a in e["attacks"] if attack_by_id[a]["kind"] in ("projectile", "retaliate")]
        pairs = []
        if len(ranged) == 1 and len(proj_attacks) == 1:
            pairs = [(proj_attacks[0], ranged[0])]
        else:
            free = list(ranged)
            for a in proj_attacks:
                best, score = None, 0
                for r in free:
                    s = len(tokens(a["projectile"]) & tokens(basename(r["entity_file"])))
                    if s > score:
                        best, score = r, s
                if best:
                    pairs.append((a, best))
                    free.remove(best)
            unmatched = [a["id"] for a in proj_attacks if a not in [p[0] for p in pairs]]
            if unmatched or free:
                notes.append("%s: could not match attacks %s to data.wak files %s" % (
                    e["id"], unmatched, [r["entity_file"] for r in free]))
        for a, r in pairs:
            if r.get("frames_between"):
                # Noita also keeps the creature in its attack state for state_frames before it can act again
                verify(a, "cooldown_frames", int(r["frames_between"]) + int(r.get("state_frames") or 0))
            if sounds:
                verify(a, "sound", first_sound(roots, ["attack_shoot", "voc_shoot", "_throw", "_voc_attack", "voc_attack"]))
            if r.get("max_distance_px"):
                verify(a, "range_tiles", round(r["max_distance_px"] * PIXEL_SCALE / TILE, 2))
            if r.get("count_min") and r.get("count_max"):
                verify(a, "count", [int(r["count_min"]), int(r["count_max"])])
            elif r.get("count_min") is None and r.get("count_max") is None and a["count"] == [1, 1]:
                verify(a, "count", [1, 1])  # attributes absent: Noita's default of one projectile per shot
            p = proj_by_id.get(a["projectile"])
            pf = r.get("projectile")
            if not p or not pf:
                notes.append("%s: no projectile facts for %s (%s)" % (e["id"], a["id"], r.get("projectile_error", "")))
                continue
            # a projectile row shared by several enemies keeps the file it was first verified with
            owner = claimed.get(p["id"])
            if owner is None and filled(p.get("noita_file")) and "noita_file" not in p.get("_unverified", {}):
                owner = claimed[p["id"]] = (p["noita_file"], "an earlier stage")
            if owner is not None and owner[0] != r["entity_file"]:
                shared = p
                p = own_projectile_row(shared, r["entity_file"], e["id"], a)
                notes.append("%s: %s fires %s, not %s's %s (from %s): moved to its own projectile row %s" % (
                    e["id"], a["id"], r["entity_file"], shared["id"], owner[0], owner[1], p["id"]))
            claimed[p["id"]] = (r["entity_file"], e["id"])
            verify(p, "noita_file", r["entity_file"])
            if pf.get("sprite"):
                verify(p, "sprite", pf["sprite"])
            if pf.get("speed_min") is not None:
                speed = (pf["speed_min"] + (pf.get("speed_max") or pf["speed_min"])) / 2.0
                verify(p, "speed", round(speed * PIXEL_SCALE / FPS, 3))
            verify(p, "gravity", round((pf.get("gravity_y") or 0.0) * PIXEL_SCALE / (FPS * FPS), 4))
            if pf.get("lifetime_frames") is not None:
                verify(p, "lifetime_frames", int(pf["lifetime_frames"]))
            verify(p, "explosion_radius", round((pf.get("explosion_radius_px") or 0.0) * PIXEL_SCALE / TILE, 2))
            if sounds:
                root = pf.get("audio_root")
                verify(p, "audio", root if root and any(x.startswith(root + "/") for x in sounds) else "none")
                ex = pf.get("explosion_sound")
                verify(p, "explosion_sound", ex if ex in sounds else "none")
            if "effect" in p.get("_unverified", {}):
                eff, why = derive_effect(pf, os.path.basename(r["entity_file"]))
                if eff:
                    if eff != p["effect"]:
                        notes.append("%s: effect %s (sheet guessed %s) from %s" % (p["id"], eff, p["effect"], why))
                    verify(p, "effect", eff)
                    p.setdefault("_sources", {})["effect"] = why
                else:
                    notes.append("%s: effect not decided: %s" % (p["id"], why))

    for s in (enemies, attacks, projectiles):
        save(args.sheets, s)
    print("applied facts up to stage %s" % args.stage)
    for n in notes:
        print("  NOTE " + n)
    print("Run: python tools/preflight.py --gate %s" % args.stage)


if __name__ == "__main__":
    sys.exit(main())
