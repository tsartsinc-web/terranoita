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
import math
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

# sound events an attack of each kind starts with, most specific first (Noita names them per creature folder)
SOUND_NAMES = {
    "melee": ["attack_melee", "attack_bite", "limb_attack", "_voc_attack", "voc_attack"],
    "lunge": ["attack_dash", "voc_attack", "_voc_attack"],
    "projectile": ["attack_shoot", "attack_ranged", "shoot", "voc_shoot", "_throw", "_voc_attack", "voc_attack"],
    "retaliate": ["shoot", "attack_shoot", "attack_ranged"],
    "summon": ["attack_ranged", "attack_shoot", "shoot", "duplicate", "voc_attack"],
    "heal": ["attack_ranged", "attack_shoot", "shoot"],
    "support": ["attack_ranged", "attack_shoot", "shoot"],
    "aura": ["attack_aura", "aura"],
    "death_explosion": ["death_buildup", "explode", "explosion"],
}

# components that move a creature some other way than CharacterPlatformingComponent
MOVERS = {"WormComponent", "WormAIComponent", "PhysicsAIComponent", "AdvancedFishAIComponent", "FishAIComponent",
          "IKLimbWalkerComponent", "IKLimbsAnimatorComponent", "LimbBossComponent", "CrawlerAnimalComponent",
          "BossDragonComponent", "LevitationComponent", "TeleportComponent", "VelocityComponent",
          "SimplePhysicsComponent", "PhysicsBodyComponent", "CharacterPlatformingComponent"}
# components that hurt or affect things by themselves (auras, explosions, scripted attacks)
ATTACKERS = {"AreaDamageComponent", "GameAreaEffectComponent", "ExplosionComponent",
             "DamageNearbyEntitiesComponent", "AIAttackComponent", "IKLimbAttackerComponent",
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


# components that move, draw or hurt plainly: no status effect of their own
NO_EFFECT_COMPONENTS = {"SpriteComponent", "AudioComponent", "VelocityComponent", "HomingComponent", "VariableStorageComponent",
                        "LuaComponent", "VerletPhysicsComponent", "VerletWeaponComponent", "LightComponent",
                        "ParticleEmitterComponent", "AreaDamageComponent", "PhysicsBodyComponent", "PhysicsImageShapeComponent",
                        "PhysicsBody2Component", "ExplodeOnDamageComponent", "DamageModelComponent", "HitEffectComponent",
                        "LifetimeComponent", "SpriteParticleEmitterComponent", "HitboxComponent", "GenomeDataComponent",
                        "SimplePhysicsComponent", "CellEaterComponent", "LaserEmitterComponent"}
# glow and debris materials (sparks, fading plasma, blood and meat of a sausage): not effects
VISUAL_MATERIALS = ("spark", "plasma_fading", "blood", "meat", "smoke", "glowing")


def components(f, name=None, top=False):
    """Behaviour components of an entity (top=True: the entity itself, not its child entities). Components a file
    removes from its base (_remove_from_base="1") do not count."""
    for c in f.get("components") or []:
        if (name is None or c["component"] == name) and not (top and c.get("entity")) and \
                c["attrs"].get("_remove_from_base") != "1":
            yield c


DOCS = {}   # component -> {attribute: default}, from the facts file's _component_docs


def load_docs(facts):
    # member lines: "<type> <name> <default> [min, max] "<description>""; the type is one word except "unsigned int"
    for comp, text in (facts.get("_component_docs") or {}).items():
        d = DOCS.setdefault(comp, {})
        for line in text.split("\n"):
            t = line.split()
            if not t or t[0].startswith("-"):
                continue
            i = 2 if t[0] == "unsigned" else 1
            if len(t) > i + 1:
                d.setdefault(t[i], t[i + 1])


def attr(f, comp, name, top=True):
    """An attribute of the creature's first such component as a number; absent: Noita's documented DEFAULT."""
    c = next(components(f, comp, top), None)
    if c is not None and name in c["attrs"]:
        return num(c["attrs"][name])
    if name in DOCS.get(comp, {}):
        return num(DOCS[comp][name])
    raise KeyError("%s.%s has no value and no documented default" % (comp, name))


def has(f, comp, top=True):
    return next(components(f, comp, top), None) is not None


DAMAGE_TYPE_NAMES = {"DAMAGE_MELEE": "melee", "DAMAGE_PROJECTILE": "projectile", "DAMAGE_EXPLOSION": "explosion",
                     "DAMAGE_ELECTRICITY": "electricity", "DAMAGE_FIRE": "fire", "DAMAGE_DRILL": "drill",
                     "DAMAGE_SLICE": "slice", "DAMAGE_ICE": "ice", "DAMAGE_HEALING": "healing",
                     "DAMAGE_PHYSICS_HIT": "physics", "DAMAGE_RADIOACTIVE": "radioactive", "DAMAGE_POISON": "poison",
                     "DAMAGE_CURSE": "curse", "DAMAGE_HOLY": "holy", "DAMAGE_OVEREATING": "overeating"}


def damage_type(v):
    return DAMAGE_TYPE_NAMES.get((v or "DAMAGE_MELEE").strip().upper(), (v or "melee").lower().replace("damage_", ""))


# a design value where data.wak has none (engine-private timing): kept out of _unverified, said so in _sources
PLACEHOLDER = "design placeholder, no data in data.wak (engine-private); confirm in the PC autotest"


def placeholder(row, col, value, why):
    row[col] = value
    row.setdefault("_unverified", {}).pop(col, None)
    row.setdefault("_sources", {})[col] = PLACEHOLDER + ": " + why


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
        if c["component"] == "HitEffectComponent" and c["attrs"].get("effect_hit") == "LOAD_CHILD_ENTITY":
            child = basename(c["attrs"].get("value_string"))
            for word, eff in (("neutraliz", "neutralize"), ("teleport", "teleport"), ("polymorph", "polymorph"),
                              ("blind", "blind"), ("freeze", "ice"), ("frozen", "ice"), ("fire", "fire"), ("poison", "poison")):
                if word in child:
                    return eff, "%s: HitEffectComponent loads %s on hit" % (file, c["attrs"].get("value_string"))
    materials = []
    for c in comps:
        for n in all_nodes(c):
            for k, v in n["attrs"].items():
                if "material" in k and k != "audio_physics_material" and isinstance(v, str) and v and not v.isdigit():
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
    # what is left once visuals, motion and plain damage are set aside decides "none"
    seen = sorted(set(c["component"] for c in comps) - NO_EFFECT_COMPONENTS)
    visual = all(any(m.startswith(w) for w in VISUAL_MATERIALS) for m in materials)
    if (seen == ["ProjectileComponent"] or not seen) and visual:
        return "none", "%s: plain damage, no effect, material or damage_by_type" % file
    return None, "components %s, materials %s" % (", ".join(seen), ", ".join(sorted(set(materials))) or "none")


PASSIVE = {"VelocityComponent", "SimplePhysicsComponent"}   # things that only fall or get pushed


def is_static(f):
    """True when nothing moves the creature by itself (crystals, turrets, traps, nests, plants): no mover component
    other than plain physics, and a crawler only if its speed is 0."""
    if f.get("movement") or not f.get("components"):
        return False
    for c in components(f):
        if c["component"] == "CrawlerAnimalComponent":
            if num(c["attrs"].get("speed"), 1) != 0:
                return False
        elif c["component"] in MOVERS and c["component"] not in PASSIVE:
            return False
    return True


def spawned_files(f):
    """Entity files a creature's scripts and components name: what it summons or releases."""
    out = []
    for files in (f.get("script_entities") or {}).values():
        out += files or []
    for c in components(f):
        for n in all_nodes(c):
            out += [v for v in n["attrs"].values() if isinstance(v, str) and v.startswith("data/entities/animals/") and v.endswith(".xml")]
    return list(dict.fromkeys(out))


SHOOTING_KINDS = ("projectile", "retaliate", "heal", "support")


def squash(s):
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def match_score(attack, entity_file, pf=None):
    """How well a sheet attack fits a data.wak projectile file: shared words, plus one if either name contains the
    other once underscores are dropped (machine_gun / machinegun_bullet_tank), plus two when the wiki's projectile
    damage is the file's ProjectileComponent damage x 25 (buckshot 0.27 -> 6.75)."""
    name, file = attack["projectile"] if attack["projectile"] not in (None, "none") else attack["id"].split(".", 1)[1], basename(entity_file)
    score = len(tokens(name) & tokens(file))
    a, b = squash(name), squash(file)
    if a and b and (a in b or b in a):
        score += 1
    wiki = (attack.get("damage") or {}).get("projectile")
    if pf and pf.get("damage") and isinstance(wiki, (int, float)) and abs(pf["damage"] * 25 - wiki) < 0.01:
        score += 2
    elif pf and pf.get("damage") == 0 and isinstance(wiki, (int, float)) and wiki > 0:
        score -= 1      # a harmless shot is not the wiki's damaging one
    return score


def script_shots(f):
    """Projectiles a creature's Lua scripts load: [(entity_file, script attribute, execute_every_n_frame)]."""
    out = []
    for c in components(f, "LuaComponent"):
        for k, script in c["attrs"].items():
            if not k.startswith("script_"):
                continue
            for x in (f.get("script_entities") or {}).get(script) or []:
                if x.startswith("data/entities/projectiles/") and x.endswith(".xml") and "explosion" not in basename(x):
                    out.append((x, k, int(num(c["attrs"].get("execute_every_n_frame"), 1))))
    return out


def new_ranged_attack(enemy, r):
    pf = r.get("projectile") or {}
    a = {"id": "%s.%s" % (enemy["id"], basename(r["entity_file"])), "enemy": enemy["id"], "kind": "projectile",
         "projectile": "none", "summons": [], "effect": "none", "count": [1, 1],
         "damage": {"projectile": round((pf["damage"] if pf.get("damage") is not None else
                                         num(DOCS.get("ProjectileComponent", {}).get("damage"), 1)) * 25.0, 2)}, "per_frames": "hit",
         "cooldown_frames": None, "range_tiles": None, "lunge_speed": 0, "sound": None,
         "wiki_text": "From data.wak: %s ranged attack %s (not on the wiki)" % (r.get("source"), r["entity_file"]),
         "stage": enemy["stage"], "_unverified": {}}
    for c in pf.get("components") or []:
        ex = next((ch for ch in c.get("children") or [] if ch["component"] == "config_explosion"), None) \
            if c["component"] == "ProjectileComponent" else None
        if ex and num(ex["attrs"].get("explosion_radius")) > 0 and num(ex["attrs"].get("damage")) > 0:
            a["damage"]["explosion"] = round(num(ex["attrs"]["damage"]) * 25, 2)
    return a


def has_own_attacks(f):
    """Anything in the entity that can hurt: AnimalAI attacks switched on, attack components, an explosion that does
    damage, or a script that names a projectile or creature file."""
    if f.get("ranged") or f.get("dash"):
        return True
    for c in components(f):
        a = c["attrs"]
        if c["component"] == "AnimalAIComponent" and any(a.get(k) == "1" for k in (
                "attack_melee_enabled", "attack_dash_enabled", "attack_ranged_enabled")):
            return True
        if c["component"] == "ExplodeOnDamageComponent":
            ex = next((ch for ch in c.get("children") or [] if ch["component"] == "config_explosion"), None)
            if ex is None or num(ex["attrs"].get("damage"), 1) > 0:
                return True
        elif c["component"] == "LuaComponent":
            named = [x for s_, files in (f.get("script_entities") or {}).items() if s_ in a.values() for x in files or []]
            if any(x.startswith(("data/entities/projectiles/", "data/entities/animals/")) for x in named):
                return True
        elif c["component"] in ATTACKERS:
            return True
    return False


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
    load_docs(facts)
    enemies, attacks, projectiles = (load(args.sheets, n) for n in ("enemies", "attacks", "projectiles"))
    attack_by_id = {a["id"]: a for a in attacks["rows"]}
    proj_by_id = {p["id"]: p for p in projectiles["rows"]}
    notes = []
    claimed = {}    # projectile id -> (noita_file, who set it)

    def own_projectile_row(shared, entity_file, enemy_id, attack):
        """A copy of a shared projectile row for the enemy whose data.wak file differs (or a new row when the attack
        had none, like heal shots); the attack is repointed."""
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
            if shared is None:
                shared = {"id": "none", "name_en": basename(entity_file).replace("_", " ").capitalize() + " projectile",
                          "effect": "heal" if attack["kind"] == "heal" else "none", "terrain": "none"}
                name = shared["name_en"]
            else:
                name = shared["name_en"] + " (" + basename(entity_file) + ")"
            row = {k: v for k, v in shared.items() if not k.startswith("_")}
            row.update(id=pid, name_en=name, used_by=[],
                       noita_file=None, sprite=None, speed=None, gravity=None, lifetime_frames=None,
                       explosion_radius=None, audio=None, explosion_sound=None, stage=attack["stage"])
            row["_unverified"] = {"effect": "copied from %s; confirm in %s" % (shared["id"], entity_file)}
            projectiles["rows"].append(row)
            proj_by_id[pid] = row
        if shared is not None and enemy_id in shared.get("used_by", []):
            shared["used_by"].remove(enemy_id)
        if enemy_id not in row["used_by"]:
            row["used_by"].append(enemy_id)
        if STAGES.index(attack["stage"]) < STAGES.index(row["stage"]):
            row["stage"] = attack["stage"]
        attack["projectile"] = pid
        claimed[pid] = (entity_file, enemy_id)
        return row

    def fill_damage(a, pf):
        """An attack the wiki gives no damage: its projectile's ProjectileComponent damage (default from the docs) and
        the explosion's config_explosion damage, x 25 as Noita shows them."""
        if a.get("damage"):
            return
        d = pf.get("damage") if pf.get("damage") is not None else num(DOCS.get("ProjectileComponent", {}).get("damage"), 1)
        dmg = {"projectile": round(d * 25, 2)}
        for c in pf.get("components") or []:
            if c["component"] == "ProjectileComponent":
                ex = next((ch for ch in c.get("children") or [] if ch["component"] == "config_explosion"), None)
                if ex and num(ex["attrs"].get("explosion_radius")) > 0 and num(ex["attrs"].get("damage")) > 0:
                    dmg["explosion"] = round(num(ex["attrs"]["damage"]) * 25, 2)
        verify(a, "damage", dmg)
        a.setdefault("_sources", {})["damage"] = "its projectile's ProjectileComponent (data.wak); not on the wiki"

    def fill_projectile(p, pf, file):
        """A projectile row's numbers from its file's facts; absent ProjectileComponent attributes take Noita's defaults."""
        if pf.get("sprite"):
            verify(p, "sprite", pf["sprite"])
        elif pf.get("components") is not None and not any(c["component"] in ("SpriteComponent", "PhysicsImageShapeComponent")
                                                         and c["attrs"].get("image_file") for c in pf["components"]):
            verify(p, "sprite", "none")     # drawn by particles or light only
        elif pf.get("components"):
            imgs = [c["attrs"]["image_file"] for c in pf["components"] if c["attrs"].get("image_file")]
            verify(p, "sprite", imgs[0])    # verlet tongues/tentacles: their first segment's image
        lo = pf.get("speed_min") if pf.get("speed_min") is not None else num(DOCS.get("ProjectileComponent", {}).get("speed_min"), 60)
        hi = pf.get("speed_max") if pf.get("speed_max") is not None else num(DOCS.get("ProjectileComponent", {}).get("speed_max"), lo)
        verify(p, "speed", round((lo + hi) / 2.0 * PIXEL_SCALE / FPS, 3))
        verify(p, "gravity", round((pf.get("gravity_y") or 0.0) * PIXEL_SCALE / (FPS * FPS), 4))
        vel = next((c for c in pf.get("components") or [] if c["component"] == "VelocityComponent"), None)
        docs = DOCS.get("VelocityComponent", {})
        va = vel["attrs"] if vel else {}
        verify(p, "drag", num(va.get("air_friction"), num(docs.get("air_friction"), 0.55)) if vel else 0.0)
        verify(p, "max_speed", round(num(va.get("terminal_velocity"), num(docs.get("terminal_velocity"), 1000)) * PIXEL_SCALE / FPS, 3))
        mats = [c["attrs"].get("emitted_material_name") for c in pf.get("components") or []
                if c["component"] == "ParticleEmitterComponent" and c["attrs"].get("emitted_material_name")]
        verify(p, "particle", mats[0] if mats else "none")
        life = pf.get("lifetime_frames")
        verify(p, "lifetime_frames", int(life if life is not None else num(DOCS.get("ProjectileComponent", {}).get("lifetime"), -1)))
        verify(p, "explosion_radius", round((pf.get("explosion_radius_px") or 0.0) * PIXEL_SCALE / TILE, 2))
        if sounds:
            root = pf.get("audio_root")
            verify(p, "audio", root if root and any(x.startswith(root + "/") for x in sounds) else "none")
            ex = pf.get("explosion_sound")
            verify(p, "explosion_sound", ex if ex in sounds else "none")
        if "effect" in p.get("_unverified", {}):
            eff, why = derive_effect(pf, os.path.basename(file))
            if eff:
                if eff != p["effect"]:
                    notes.append("%s: effect %s (sheet guessed %s) from %s" % (p["id"], eff, p["effect"], why))
                verify(p, "effect", eff)
                p.setdefault("_sources", {})["effect"] = why
            else:
                notes.append("%s: effect not decided: %s" % (p["id"], why))

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
        elif has(f, "WormComponent") and not has(f, "AnimalAIComponent"):
            # worms: WormAIComponent speeds are Noita px per frame; WormComponent gravity/acceleration are px/frame
            # gained per second (see MODLOG: unit reasoning, confirmed by the autotest's leap heights)
            for col in ("walks", "flies", "jumps"):
                verify(e, col, False)
            for col in ("fly_speed", "fly_up_speed", "jump_speed"):
                verify(e, col, 0.0)
            verify(e, "run_speed", round(attr(f, "WormAIComponent", "speed_hunt") * PIXEL_SCALE, 3))
            verify(e, "roam_speed", round(attr(f, "WormAIComponent", "speed") * PIXEL_SCALE, 3))
            verify(e, "turn_rate", attr(f, "WormAIComponent", "direction_adjust_speed_hunt"))
            verify(e, "roam_turn_rate", attr(f, "WormAIComponent", "direction_adjust_speed"))
            verify(e, "gravity", round(attr(f, "WormComponent", "gravity") * PIXEL_SCALE / FPS, 4))
            verify(e, "accel", round(attr(f, "WormComponent", "acceleration") * PIXEL_SCALE / FPS, 4))
            verify(e, "sight_tiles", round(attr(f, "WormAIComponent", "hunt_box_radius") * PIXEL_SCALE / TILE, 1))
            if not f.get("hitbox_noita_px"):
                d = int(round(2 * attr(f, "WormComponent", "hitbox_radius") * PIXEL_SCALE))
                verify(e, "hitbox", [d, d])
            # head, body x n, tail: the creature's own SpriteComponents in order (ui health bars left out)
            parts = [c["attrs"]["image_file"] for c in components(f, "SpriteComponent", top=True)
                     if c["attrs"].get("image_file") and "/ui_gfx/" not in c["attrs"]["image_file"]]
            if len(parts) >= 2:
                verify(e, "body_sprite", parts[1] if len(parts) > 2 else "none")
                verify(e, "tail_sprite", parts[-1])
                verify(e, "segments", len(parts) - 1)
                verify(e, "segment_spacing", round(attr(f, "WormComponent", "part_distance") * PIXEL_SCALE, 2))
        elif has(f, "PhysicsAIComponent") and has(f, "PhysicsBodyComponent") and not f.get("movement"):
            # lukki and the leggy chest: a physics body pushed toward its path by PhysicsAIComponent. The push is
            # force_coeff x (target vector, at most target_vec_max_len), capped at force_max, against a damping of
            # force_balancing_coeff x velocity: it settles at push/damping px/s and closes the gap at k_d per second.
            k_d = attr(f, "PhysicsAIComponent", "force_balancing_coeff")
            push = min(attr(f, "PhysicsAIComponent", "force_coeff") * attr(f, "PhysicsAIComponent", "target_vec_max_len"),
                       attr(f, "PhysicsAIComponent", "force_max"))
            speed = round(push / k_d * PIXEL_SCALE / FPS, 3)
            for col in ("run_speed", "fly_speed", "fly_up_speed"):
                verify(e, col, speed)
            verify(e, "accel", round(1 - math.exp(-k_d / FPS), 4))
            verify(e, "gravity", 0.0 if attr(f, "PhysicsAIComponent", "levitate") >= 1 else
                   round(600 * PIXEL_SCALE / (FPS * FPS), 4))
            ai = "AnimalAIComponent" if has(f, "AnimalAIComponent") else "PathFindingComponent"
            verify(e, "walks", attr(f, ai, "can_walk") >= 1)
            verify(e, "flies", attr(f, ai, "can_fly") >= 1)
            verify(e, "jumps", attr(f, "PathFindingComponent", "can_jump") >= 1)
            verify(e, "jump_speed", round(attr(f, "PathFindingComponent", "jump_speed") * PIXEL_SCALE / FPS, 3))
            if has(f, "AnimalAIComponent"):
                sight = attr(f, "AnimalAIComponent", "creature_detection_range_x")
            elif has(f, "IKLimbAttackerComponent", top=False):
                sight = attr(f, "IKLimbAttackerComponent", "targeting_radius", top=False)   # its attack leg's reach
            else:
                sight = 50.0
            verify(e, "sight_tiles", round(sight * PIXEL_SCALE / TILE, 1))
        elif is_static(f):
            for col in ("walks", "flies", "jumps"):
                verify(e, col, False)
            for col in ("run_speed", "fly_speed", "fly_up_speed", "accel", "gravity", "jump_speed"):
                verify(e, col, 0.0)
            # it notices the player as far as its AnimalAIComponent sees, else as far as its attacks reach, else 50 px
            reach = [r.get("max_distance_px") or 0 for r in f.get("ranged") or []]
            if has(f, "AnimalAIComponent"):
                sight = attr(f, "AnimalAIComponent", "creature_detection_range_x")
            else:
                sight = max(reach) if reach and max(reach) > 0 else 50.0
            verify(e, "sight_tiles", round(sight * PIXEL_SCALE / TILE, 1))
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
        if e.get("dmg_mult") and has(f, "DamageModelComponent"):
            # Noita's damage_multipliers default every type to 1 (ConfigDamagesByType)
            for k in e["dmg_mult"]:
                e["dmg_mult"][k] = dm.get(k, 1.0)
            e["_unverified"].pop("dmg_mult", None)

        if not e["attacks"] and "attacks" in e.get("_unverified", {}) and f.get("components") and not has_own_attacks(f):
            e["_unverified"].pop("attacks")
            e.setdefault("_sources", {})["attacks"] = "%s: no ranged, dash, aura, explosion or script attack" % f["entity"]

        # summons fired as a projectile that becomes the creature (bigbat's bat.xml: ProjectileComponent spawn_entity)
        for aid in e["attacks"]:
            a = attack_by_id[aid]
            if a["kind"] != "summon":
                continue
            for r in f.get("ranged") or []:
                spawn = [c["attrs"].get("spawn_entity") for c in (r.get("projectile") or {}).get("components") or []
                         if c["component"] == "ProjectileComponent" and c["attrs"].get("spawn_entity")]
                ids = [x["id"] for x in enemies["rows"] if x.get("noita_entity") in spawn]
                if ids:
                    verify(a, "summons", ids)
                    verify(a, "cooldown_frames", int(r["frames_between"]) + int(r.get("state_frames") or 0))
                    verify(a, "range_tiles", round(r["max_distance_px"] * PIXEL_SCALE / TILE, 2))
                    verify(a, "count", [int(r["count_min"]), int(r["count_max"])])
                    a.setdefault("_sources", {})["summons"] = "%s fires %s, which spawns %s" % (f["entity"], r["entity_file"], spawn[0])
                    r["_summon"] = True
        f["ranged"] = [r for r in f.get("ranged") or [] if not r.pop("_summon", False)]

        # summons: the creatures its spawn scripts or components name
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
            if a["kind"] == "summon" and a.get("summons") and "summons" not in a.get("_unverified", {}):
                lua = [c for c in components(f, "LuaComponent")
                       if any(by_file.get(x) in a["summons"] for k, v in c["attrs"].items() if k.startswith("script_")
                              for x in (f.get("script_entities") or {}).get(v) or [])]
                if lua:
                    every = int(num(lua[0]["attrs"].get("execute_every_n_frame"), 1))
                    placeholder(a, "cooldown_frames", max(1, every), "its spawn script runs every %d frames and may not release each time" % every)
                    placeholder(a, "count", a.get("count") or [1, 1], "the spawn script decides how many")
                    placeholder(a, "range_tiles", 15, "the spawn script decides when (design: 15 tiles, inside a Terraria screen)")
                    if "attacks" in e.get("_unverified", {}):
                        e["_unverified"].pop("attacks")
                        e.setdefault("_sources", {})["attacks"] = "its spawn script releases %s" % ", ".join(a["summons"])

        by_file = {x.get("noita_entity"): x["id"] for x in enemies["rows"] if x.get("noita_entity")}

        # spirit auras: a LuaComponent running data/scripts/animals/spirit_aura_<effect>.lua every N frames
        for c in components(f, "LuaComponent", top=True):
            m = re.search(r"spirit_aura_(\w+)\.lua$", c["attrs"].get("script_source_file") or "")
            if not m:
                continue
            aid = "%s.aura" % e["id"]
            a = attack_by_id.get(aid)
            if a is None:
                a = {"id": aid, "enemy": e["id"], "kind": "aura", "projectile": "none", "summons": [], "count": [1, 1],
                     "damage": {}, "effect": "none", "per_frames": None, "cooldown_frames": None, "range_tiles": None,
                     "lunge_speed": 0, "sound": None, "stage": e["stage"], "_unverified": {},
                     "wiki_text": "From data.wak: %s (aura script, not on the wiki)" % c["attrs"]["script_source_file"]}
                attacks["rows"].append(a)
                attack_by_id[aid] = a
                e["attacks"].append(aid)
                notes.append("%s: added %s from %s" % (e["id"], aid, c["attrs"]["script_source_file"]))
            every = int(num(c["attrs"].get("execute_every_n_frame"), 1))
            verify(a, "effect", m.group(1) if m.group(1) in ("weak", "slime", "confuse", "berserk") else "none")
            verify(a, "cooldown_frames", max(1, every))
            verify(a, "per_frames", "%dF" % max(1, every))
            placeholder(a, "range_tiles", 6, "the aura script decides its reach")
            a.setdefault("_sources", {})["effect"] = "%s runs %s every %d frames" % (f["entity"], c["attrs"]["script_source_file"], every)
            e.get("_unverified", {}).pop("attacks", None)

        # creatures a hurt script releases (giantshooter_death.lua on damage received -> slimeshooters): a retaliate
        # attack with summons
        for c in components(f, "LuaComponent", top=True):
            script = c["attrs"].get("script_damage_received")
            ids = [by_file[x] for x in (f.get("script_entities") or {}).get(script) or [] if by_file.get(x) not in (None, e["id"])]
            if not script or not ids:
                continue
            aid = "%s.split" % e["id"]
            a = attack_by_id.get(aid)
            if a is None:
                a = {"id": aid, "enemy": e["id"], "kind": "retaliate", "projectile": "none", "summons": [], "effect": "none",
                     "count": [1, 1], "damage": {}, "per_frames": "hit", "cooldown_frames": None, "range_tiles": None,
                     "lunge_speed": 0, "sound": None, "stage": e["stage"], "_unverified": {},
                     "wiki_text": "From data.wak: %s releases %s when hurt" % (script, ", ".join(ids))}
                attacks["rows"].append(a)
                attack_by_id[aid] = a
                e["attacks"].append(aid)
                notes.append("%s: added %s (%s)" % (e["id"], aid, script))
            verify(a, "summons", ids)
            verify(a, "range_tiles", 0)
            placeholder(a, "cooldown_frames", 60, "the hurt script decides how often; at most one a second")
            placeholder(a, "count", [1, 1], "the hurt script decides how many")
            a.setdefault("_sources", {})["summons"] = "%s %s" % (f["entity"], script)

        # death explosions: ExplodeOnDamageComponent on death (radius and damage from its config_explosion), or the
        # explosion entity a death script loads (its numbers come with script_projectiles in the next facts run)
        for aid in e["attacks"]:
            a = attack_by_id[aid]
            if a["kind"] != "death_explosion":
                continue
            ex = next((c for c in components(f, "ExplodeOnDamageComponent", top=True)
                       if num(c["attrs"].get("explode_on_death_percent"), 1) > 0), None)
            cfg = next((ch for ch in (ex or {}).get("children") or [] if ch["component"] == "config_explosion"), None)
            if ex is not None:
                ca = (cfg or {}).get("attrs", {})
                radius = num(ca.get("explosion_radius"), 20.0)
                verify(a, "range_tiles", round(radius * PIXEL_SCALE / TILE, 2))
                verify(a, "damage", {"explosion": round(num(ca.get("damage"), 5.0) * 25, 2)})
                verify(a, "cooldown_frames", 0)
                a.setdefault("_sources", {})["range_tiles"] = "%s ExplodeOnDamageComponent config_explosion" % f["entity"]
                continue
            sp = (f.get("script_projectiles") or {})
            loaded = [x for fs in (f.get("script_entities") or {}).values() for x in fs or [] if "explosion" in x]
            pf = next((sp[x] for x in loaded if x in sp), None)
            if pf and pf.get("explosion_radius_px") is not None:
                verify(a, "range_tiles", round(pf["explosion_radius_px"] * PIXEL_SCALE / TILE, 2))
                verify(a, "cooldown_frames", 0)
            elif loaded:
                verify(a, "cooldown_frames", 0)
                placeholder(a, "range_tiles", 2, "radius of %s (death script), dumped by the next facts run" % loaded[0])

        # auras from the creature's own AreaDamageComponent / DamageNearbyEntitiesComponent, in order; a data.wak aura
        # the sheet lacks gets a row
        auras = [attack_by_id[x] for x in e["attacks"] if attack_by_id[x]["kind"] == "aura"]
        sources = [c for c in components(f, top=True) if c["component"] in ("AreaDamageComponent", "DamageNearbyEntitiesComponent")]
        if not sources and auras:   # a damage field carried by a child entity (failed_alchemist_b)
            sources = [c for c in components(f) if c["component"] in ("AreaDamageComponent", "DamageNearbyEntitiesComponent")][:len(auras)]
        for i, c in enumerate(sources):
            if i < len(auras):
                a = auras[i]
            else:
                a = {"id": "%s.touch" % e["id"], "enemy": e["id"], "kind": "aura", "projectile": "none", "summons": [], "effect": "none",
                     "count": [1, 1], "damage": {}, "per_frames": None, "cooldown_frames": None, "range_tiles": None,
                     "lunge_speed": 0, "sound": None, "stage": e["stage"], "_unverified": {},
                     "wiki_text": "From data.wak: %s (not on the wiki)" % c["component"]}
                if a["id"] in attack_by_id:
                    continue
                attacks["rows"].append(a)
                attack_by_id[a["id"]] = a
                e["attacks"].append(a["id"])
                notes.append("%s: added aura %s from its %s" % (e["id"], a["id"], c["component"]))
            if c["component"] == "AreaDamageComponent":
                every = int(attr(f, "AreaDamageComponent", "update_every_n_frame"))
                r = num(c["attrs"].get("circle_radius"))
                if r <= 0:
                    r = max(abs(num(c["attrs"].get(k))) for k in ("aabb_min.x", "aabb_max.x", "aabb_min.y", "aabb_max.y"))
                dmg = num(c["attrs"].get("damage_per_frame"), num(DOCS.get("AreaDamageComponent", {}).get("damage_per_frame")))
                verify(a, "damage", {damage_type(c["attrs"].get("damage_type")): round(dmg * 25, 2)})
            else:
                every = int(num(c["attrs"].get("time_between_damaging"), num(DOCS.get(c["component"], {}).get("time_between_damaging"))))
                r = num(c["attrs"].get("radius"), num(DOCS.get(c["component"], {}).get("radius")))
                lo = num(c["attrs"].get("damage_min"), num(DOCS.get(c["component"], {}).get("damage_min")))
                hi = num(c["attrs"].get("damage_max"), num(DOCS.get(c["component"], {}).get("damage_max")))
                verify(a, "damage", {damage_type(c["attrs"].get("damage_type")): [round(lo * 25, 2), round(hi * 25, 2)]})
            verify(a, "per_frames", "%dF" % max(1, every))
            verify(a, "cooldown_frames", max(1, every))
            verify(a, "range_tiles", round(r * PIXEL_SCALE / TILE, 2))
            a.setdefault("_sources", {})["damage"] = "%s %s" % (f["entity"], c["component"])

        # melee timing
        for aid in e["attacks"]:
            a = attack_by_id[aid]
            if a["kind"] == "melee" and has(f, "WormComponent") and not f.get("melee_frames_between"):
                # a worm bites what its head touches: within target_kill_radius, for bite_damage
                verify(a, "range_tiles", round(attr(f, "WormComponent", "target_kill_radius") * PIXEL_SCALE / TILE, 2))
                verify(a, "damage", {"melee": round(attr(f, "WormComponent", "bite_damage") * 25, 2)})
                placeholder(a, "cooldown_frames", 40, "WormComponent bites on contact; 40 = Terraria's hit immunity")
            elif a["kind"] == "melee" and has(f, "IKLimbAttackerComponent", top=False) and not f.get("melee_frames_between"):
                # lukki: the attack leg strikes what is within its radius (the limb's length), from the body
                verify(a, "range_tiles", round(attr(f, "IKLimbAttackerComponent", "radius", top=False) * PIXEL_SCALE / TILE, 2))
                placeholder(a, "cooldown_frames", 40, "IKLimbAttackerComponent strike cycle")
            elif a["kind"] == "melee" and f.get("melee_frames_between"):
                if not a.get("damage") and has(f, "AnimalAIComponent"):
                    verify(a, "damage", {"melee": [round(attr(f, "AnimalAIComponent", "attack_melee_damage_min") * 25, 2),
                                                   round(attr(f, "AnimalAIComponent", "attack_melee_damage_max") * 25, 2)]})
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

        # the start sound of every other attack, by kind, from the creature's own sound folders (None: not decided)
        for aid in e["attacks"]:
            a = attack_by_id[aid]
            names = SOUND_NAMES.get(a["kind"])
            if sounds and names and a.get("sound") is None:
                verify(a, "sound", first_sound(roots, names))

        # ranged: match sheet projectile attacks to data.wak ranged attacks
        pairs_script = []
        ranged = list(f.get("ranged") or [])
        if any(r.get("source") == "ai_attack" for r in ranged):
            # creatures with their own AIAttackComponents: AnimalAIComponent's built-in shot (the base files' acidshot)
            # is not what they fire
            ranged = [r for r in ranged if r.get("source") != "animal_ai"]
        proj_attacks = [attack_by_id[a] for a in e["attacks"] if attack_by_id[a]["kind"] in SHOOTING_KINDS
                        and not attack_by_id[a].get("summons")]
        pairs = []
        if len(ranged) == 1 and len(proj_attacks) == 1:
            pairs = [(proj_attacks[0], ranged[0])]
        else:
            free = list(ranged)
            for a in proj_attacks:
                best, score = None, 0
                for r in free:
                    s = match_score(a, r["entity_file"], r.get("projectile"))
                    if s > score:
                        best, score = r, s
                if best:
                    pairs.append((a, best))
                    free.remove(best)
            unmatched = [a for a in proj_attacks if a not in [p[0] for p in pairs]]
            # a harmless data.wak shot (damage 0) belongs to the one attack the wiki gives no damage
            harmless = [r for r in free if (r.get("projectile") or {}).get("damage") == 0]
            undamaged = [a for a in unmatched if not any(v for v in (a.get("damage") or {}).values() if not isinstance(v, list))
                         and not any(x for v in (a.get("damage") or {}).values() if isinstance(v, list) for x in v)]
            if len(harmless) == 1 and len(undamaged) == 1:
                pairs.append((undamaged[0], harmless[0]))
                unmatched.remove(undamaged[0])
                free.remove(harmless[0])
            if len(unmatched) == 1 and len(free) == 1:
                pairs.append((unmatched[0], free[0]))   # the only attack left fires the only file left
                unmatched, free = [], []
            if not proj_attacks and len(free) == 1 and not any(attack_by_id[x]["kind"] == "summon" for x in e["attacks"]):
                # data.wak has a ranged attack the wiki does not list: it gets its own row
                a = new_ranged_attack(e, free[0])
                attack_by_id[a["id"]] = a
                attacks["rows"].append(a)
                e["attacks"].append(a["id"])
                pairs.append((a, free[0]))
                notes.append("%s: added attack %s for %s (not on the wiki)" % (e["id"], a["id"], free[0]["entity_file"]))
                free = []
            if not unmatched and free and not any(attack_by_id[x]["kind"] == "summon" for x in e["attacks"]):
                for r in free:      # every sheet attack is paired: the other data.wak shots get their own rows
                    a = new_ranged_attack(e, r)
                    if a["id"] in attack_by_id:
                        continue
                    attack_by_id[a["id"]] = a
                    attacks["rows"].append(a)
                    e["attacks"].append(a["id"])
                    pairs.append((a, r))
                    notes.append("%s: added attack %s for %s (not on the wiki)" % (e["id"], a["id"], r["entity_file"]))
                free = []
            # attacks a Lua script fires: its projectile file, every execute_every_n_frame; on damage/touch = retaliate
            used = set(r["entity_file"] for _, r in pairs)
            shots = [x for x in script_shots(f) if x[0] not in used]
            for a in list(unmatched):
                best = max(shots, key=lambda x: match_score(a, x[0], (f.get("script_projectiles") or {}).get(x[0])), default=None)
                if best is None or (len(shots) > 1 and match_score(a, best[0]) == 0 and len(unmatched) > 1):
                    continue
                shots.remove(best)
                unmatched.remove(a)
                file, key, every = best
                event = key in ("script_damage_received", "script_collision_trigger_hit", "script_death")
                if event and a["kind"] != "retaliate":
                    a["kind"] = "retaliate"
                    a.setdefault("_sources", {})["kind"] = "%s fires %s from %s" % (f["entity"], file, key)
                if event:
                    verify(a, "cooldown_frames", 0)     # fires on every hit / touch / death
                else:
                    placeholder(a, "cooldown_frames", max(1, every), "its script runs every %d frames and may not fire each time" % every)
                placeholder(a, "count", a.get("count") or [1, 1], "the script %s decides how many" % key)
                placeholder(a, "range_tiles", e.get("sight_tiles") or 9.4, "the script decides when to fire; its sight")
                a.setdefault("_sources", {})["cooldown_frames"] = "%s LuaComponent %s every %d frames -> %s" % (f["entity"], key, every, file)
                p = proj_by_id.get(a["projectile"])
                if p is None or (p.get("noita_file") not in (None, "", file) and "noita_file" not in p.get("_unverified", {})):
                    p = own_projectile_row(p, file, e["id"], a)
                verify(p, "noita_file", file)
                claimed[p["id"]] = (file, e["id"])
                sp = (f.get("script_projectiles") or {}).get(file)
                if sp and "error" not in sp:
                    pairs_script.append((p, sp, file, a))
            # a shot AnimalAIComponent keeps switched off until a script turns it on (coward_check.lua); the base
            # files' acidshot is not one
            off = [r for r in f.get("ranged_disabled") or [] if basename(r["entity_file"]) != "acidshot"]
            if len(unmatched) == 1 and len(off) == 1:
                a, r = unmatched.pop(), off[0]
                pairs.append((a, r))
                a.setdefault("_sources", {})["cooldown_frames"] = "AnimalAIComponent ranged attack, enabled by a script"
                for col, val in (("cooldown_frames", int(r["frames_between"]) + int(r.get("state_frames") or 0)),
                                 ("range_tiles", round(r["max_distance_px"] * PIXEL_SCALE / TILE, 2)),
                                 ("count", [int(r["count_min"]), int(r["count_max"])])):
                    verify(a, col, val)
            if unmatched or free:
                notes.append("%s: could not match attacks %s to data.wak files %s" % (
                    e["id"], [a["id"] for a in unmatched], [r["entity_file"] for r in free]))
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
            if not p and pf and a["projectile"] in (None, "", "none"):
                p = own_projectile_row(None, r["entity_file"], e["id"], a)
                notes.append("%s: %s fires %s: new projectile row %s" % (e["id"], a["id"], r["entity_file"], p["id"]))
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
            fill_projectile(p, pf, r["entity_file"])
            fill_damage(a, pf)
        for p, pf, file, a in pairs_script:
            fill_projectile(p, pf, file)
            fill_damage(a, pf)

    # projectile rows no attack reached this run (filled by hand or shared): motion and look from any facts of their file
    by_file = {}
    for v in facts.values():
        if isinstance(v, dict):
            for r in (v.get("ranged") or []) + (v.get("ranged_disabled") or []):
                if r.get("projectile"):
                    by_file.setdefault(r["entity_file"], r["projectile"])
            for file, pf in (v.get("script_projectiles") or {}).items():
                by_file.setdefault(file, pf)
    for p in projectiles["rows"]:
        if STAGES.index(p["stage"]) <= limit and p.get("drag") is None and p.get("noita_file") in by_file:
            pf = by_file[p["noita_file"]]
            vel = next((c for c in pf.get("components") or [] if c["component"] == "VelocityComponent"), None)
            docs = DOCS.get("VelocityComponent", {})
            va = vel["attrs"] if vel else {}
            verify(p, "drag", num(va.get("air_friction"), num(docs.get("air_friction"), 0.55)) if vel else 0.0)
            verify(p, "max_speed", round(num(va.get("terminal_velocity"), num(docs.get("terminal_velocity"), 1000)) * PIXEL_SCALE / FPS, 3))
            mats = [c["attrs"].get("emitted_material_name") for c in pf.get("components") or []
                    if c["component"] == "ParticleEmitterComponent" and c["attrs"].get("emitted_material_name")]
            verify(p, "particle", mats[0] if mats else "none")

    for s in (enemies, attacks, projectiles):
        save(args.sheets, s)
    print("applied facts up to stage %s" % args.stage)
    for n in notes:
        print("  NOTE " + n)
    print("Run: python tools/preflight.py --gate %s" % args.stage)


if __name__ == "__main__":
    sys.exit(main())
