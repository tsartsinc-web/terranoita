"""Stage 3 sheets from the player's Noita: spells.json (gun_actions.lua) and wands.json (wand entities).

  dotnet run --project src/Terranoita.Cli -c Release -- spells <noitaDir> design/sources/noita_spells.json   (PC)
  python tools/apply_spells.py design/sources/noita_spells.json
  python tools/apply_spells.py --empty          # only (re)write the sheet schemas, keeping rows

Every number comes from the facts file. A spell whose Noita function does more than the columns describe
(if/for, other calls) gets port = "hand" and an _unverified note: someone ports it by hand (stage 3 gate stays open).
"""
import argparse
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS = os.path.join(ROOT, "design", "sheets")

SPELL_TYPES = ["projectile", "static_projectile", "modifier", "draw_many", "material", "other", "utility", "passive"]

SPELL_COLUMNS = {
    "id": {"type": "string", "desc": "Noita action id (gun_actions.lua)."},
    "name_key": {"type": "string", "desc": "Translation key, e.g. $action_bomb (names are read from the player's Noita at runtime)."},
    "name_en": {"type": "string", "desc": "English name, for people reading the sheet."},
    "type": {"type": "enum", "values": SPELL_TYPES, "desc": "Noita ACTION_TYPE_*."},
    "sprite": {"type": "string", "desc": "Inventory icon in data.wak."},
    "mana": {"type": "number", "desc": "Mana a cast costs."},
    "max_uses": {"type": "int", "desc": "Charges; -1 = unlimited."},
    "price": {"type": "number", "desc": "Shop price in Noita gold."},
    "spawn_level": {"type": "int[]", "desc": "Noita spawn tiers where it can appear (wands, shops)."},
    "spawn_probability": {"type": "number[]", "desc": "Weight per spawn_level entry."},
    "projectiles": {"type": "string[]", "desc": "Projectile files it fires (add_projectile), in order."},
    "trigger_kind": {"type": "enum", "values": ["none", "timer", "hit_world", "death"], "desc": "Trigger/timer spells: when the payload is released."},
    "trigger_file": {"type": "string", "desc": "Projectile carrying the payload, or none."},
    "trigger_draws": {"type": "int", "desc": "Spells drawn into the payload."},
    "trigger_frames": {"type": "int", "desc": "Timer spells: frames until release (0 otherwise)."},
    "draws": {"type": "int", "desc": "Spells drawn right after it into the same shot (modifiers 1, multicasts n)."},
    "reload_add": {"type": "number", "desc": "Added to the wand's recharge time, frames."},
    "config_add": {"type": "object", "desc": "Shot config fields it adds to (c.x = c.x + n), e.g. fire_rate_wait, spread_degrees."},
    "config_mul": {"type": "object", "desc": "Shot config fields it multiplies (c.x = c.x * n)."},
    "config_set": {"type": "object", "desc": "Shot config fields it sets to a number (c.x = n), e.g. pattern_degrees."},
    "game_effects": {"type": "string", "desc": "Game effect entities added to the shot (comma list), or none."},
    "shot_add": {"type": "object", "desc": "shot_effects fields it adds to (recoil_knockback)."},
    "shot_set": {"type": "object", "desc": "shot_effects fields it sets."},
    "clamps": {"type": "string[]", "desc": "Config fields Noita keeps in a range after it (speed_multiplier 0..20, x >= 0)."},
    "extra_entities": {"type": "string", "desc": "Entities attached to every projectile of the shot (comma list), or none."},
    "port": {"type": "enum", "values": ["data", "hand"], "desc": "data = the columns describe it fully; hand = its Noita function does more (see _sources.port)."},
    "stage": {"type": "enum", "values": ["3"], "desc": "Spells come with stage 3."},
}

WAND_COLUMNS = {
    "id": {"type": "string", "desc": "Entity file name without .xml."},
    "entity": {"type": "string", "desc": "Entity file in data.wak."},
    "name_key": {"type": "string", "desc": "AbilityComponent ui_name."},
    "sprite": {"type": "string", "desc": "AbilityComponent sprite_file."},
    "shuffle": {"type": "bool", "desc": "gun_config shuffle_deck_when_empty."},
    "spells_per_cast": {"type": "int", "desc": "gun_config actions_per_round."},
    "cast_delay": {"type": "number", "desc": "gunaction_config fire_rate_wait, frames."},
    "recharge_time": {"type": "number", "desc": "gun_config reload_time, frames."},
    "mana_max": {"type": "number", "desc": "AbilityComponent mana_max."},
    "mana_charge_speed": {"type": "number", "desc": "AbilityComponent mana_charge_speed, per second."},
    "capacity": {"type": "int", "desc": "gun_config deck_capacity."},
    "spread": {"type": "number", "desc": "gunaction_config spread_degrees."},
    "speed_multiplier": {"type": "number", "desc": "gunaction_config speed_multiplier."},
    "scripts": {"type": "string[]", "desc": "LuaComponent scripts (wands whose stats a script generates)."},
    "stage": {"type": "enum", "values": ["3"], "desc": "Wands come with stage 3."},
}

WAND_ATTRS = {  # column: (component, attribute, kind)
    "name_key": ("AbilityComponent", "ui_name", str),
    "sprite": ("AbilityComponent", "sprite_file", str),
    "mana_max": ("AbilityComponent", "mana_max", float),
    "mana_charge_speed": ("AbilityComponent", "mana_charge_speed", float),
    "shuffle": ("gun_config", "shuffle_deck_when_empty", bool),
    "spells_per_cast": ("gun_config", "actions_per_round", int),
    "recharge_time": ("gun_config", "reload_time", float),
    "capacity": ("gun_config", "deck_capacity", int),
    "cast_delay": ("gunaction_config", "fire_rate_wait", float),
    "spread": ("gunaction_config", "spread_degrees", float),
    "speed_multiplier": ("gunaction_config", "speed_multiplier", float),
}


SHOT_COLUMNS = {
    "id": {"type": "string", "desc": "Projectile entity file in data.wak (what spells and triggers name)."},
    "sprite": {"type": "string", "desc": "Sprite file, or none (drawn with particles)."},
    "speed_min": {"type": "number", "desc": "ProjectileComponent speed_min, Noita px/s."},
    "speed_max": {"type": "number", "desc": "ProjectileComponent speed_max, Noita px/s."},
    "spread_rad": {"type": "number", "desc": "ProjectileComponent direction_random_rad."},
    "gravity": {"type": "number", "desc": "VelocityComponent gravity_y, Noita px/s^2."},
    "air_friction": {"type": "number", "desc": "VelocityComponent air_friction."},
    "lifetime": {"type": "int", "desc": "ProjectileComponent lifetime, frames (-1 = until it hits)."},
    "lifetime_random": {"type": "int", "desc": "ProjectileComponent lifetime_randomness, frames."},
    "damage": {"type": "number", "desc": "ProjectileComponent damage (projectile part), Noita units (x25 = hp)."},
    "damage_every_frames": {"type": "int", "desc": "damage_every_x_frames: hits again while touching (0 = once)."},
    "explosion_radius": {"type": "number", "desc": "config_explosion explosion_radius, Noita px (0 = none)."},
    "explosion_damage": {"type": "number", "desc": "config_explosion damage, Noita units."},
    "explode_on_death": {"type": "bool", "desc": "on_death_explode / on_lifetime_out_explode."},
    "die_on_hit": {"type": "bool", "desc": "on_collision_die: gone when it hits a creature."},
    "penetrate": {"type": "bool", "desc": "penetrate_entities: goes through creatures."},
    "bounces": {"type": "int", "desc": "bounces_left off the ground."},
    "collide_with_world": {"type": "bool", "desc": "Hits the ground at all."},
    "knockback": {"type": "number", "desc": "knockback_force."},
    "material": {"type": "string", "desc": "Material its particle emitter leaves (emitted_material_name), or none."},
    "explosion_material": {"type": "string", "desc": "config_explosion create_cell_material, or none."},
    "audio": {"type": "string", "desc": "Noita audio event root of the shot."},
    "explosion_sound": {"type": "string", "desc": "Explosion audio event, or none."},
    "stage": {"type": "enum", "values": ["3"], "desc": "Spell projectiles come with stage 3."},
}


def comps_of(p):
    out = {}

    def walk(cs):
        for c in cs:
            out.setdefault(c["component"], c.get("attrs", {}))
            walk(c.get("children", []))
    walk(p.get("components") or [])
    return out


def num(v, default=0.0):
    try:
        return float(v)
    except (TypeError, ValueError):
        return default


def shot_row(path, p):
    c = comps_of(p)
    pc, vc, ex = c.get("ProjectileComponent", {}), c.get("VelocityComponent", {}), c.get("config_explosion", {})
    emit = next((x for x in [c.get("ParticleEmitterComponent", {})] if x.get("emitted_material_name")), {})
    row = {
        "id": path,
        "sprite": p.get("sprite") or "none",
        "speed_min": num(pc.get("speed_min", p.get("speed_min"))),
        "speed_max": num(pc.get("speed_max", p.get("speed_max"))),
        "spread_rad": num(pc.get("direction_random_rad")),
        "gravity": num(vc.get("gravity_y", p.get("gravity_y"))),
        "air_friction": num(vc.get("air_friction")),
        "lifetime": int(num(pc.get("lifetime", p.get("lifetime_frames")), -1)),
        "lifetime_random": int(num(pc.get("lifetime_randomness"))),
        "damage": num(pc.get("damage", p.get("damage"))),
        "damage_every_frames": int(num(pc.get("damage_every_x_frames"))),
        "explosion_radius": num(ex.get("explosion_radius", p.get("explosion_radius_px"))),
        "explosion_damage": num(ex.get("damage")),
        "explode_on_death": pc.get("on_death_explode", "0") == "1" or pc.get("on_lifetime_out_explode", "0") == "1",
        "die_on_hit": pc.get("on_collision_die", "1") == "1",
        "penetrate": pc.get("penetrate_entities", "0") == "1",
        "bounces": int(num(pc.get("bounces_left"))),
        "collide_with_world": pc.get("collide_with_world", "1") == "1",
        "knockback": num(pc.get("knockback_force")),
        "material": emit.get("emitted_material_name") or "none",
        "explosion_material": ex.get("create_cell_material") or "none",
        "audio": p.get("audio_root") or "none",
        "explosion_sound": p.get("explosion_sound") or "none",
        "stage": "3",
        "_unverified": {} if pc else {"all": "no ProjectileComponent: a special entity (laser, cloud, summon...), port by hand"},
        "_sources": {"all": "projectile entity via tncli spells"},
    }
    return row


SHOTS_DESC = "Projectiles the player's spells fire (stage 3), from the player's Noita entity files."


def load(name):
    path = os.path.join(SHEETS, name + ".json")
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    return None


def write(name, desc, columns, rows):
    with open(os.path.join(SHEETS, name + ".json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"sheet": name, "description": desc, "key": "id", "columns": columns, "rows": rows},
                  f, ensure_ascii=False, indent=1)
        f.write("\n")


def spell_row(sid, s):
    trig = s.get("triggers") or []
    t = trig[0] if trig else None
    cset = dict(s.get("config_set") or {})
    extra = cset.pop("extra_entities+", None)
    effects = cset.pop("game_effect_entities+", None)
    numbers = {}
    for k in list(cset):
        try:
            numbers[k] = float(cset[k])
        except ValueError:
            continue
        cset.pop(k)
    why = []
    if s.get("conditional"):
        why.append("has if/for")
    if s.get("unparsed"):
        why.append("statements: " + " | ".join(x.replace("\n", " ")[:80] for x in s["unparsed"][:4]))
    if s.get("calls"):
        why.append("calls " + ", ".join(s["calls"]))
    if cset:
        why.append("sets " + ", ".join("c.%s = %s" % kv for kv in cset.items()))
    if len(trig) > 1:
        why.append("%d triggers" % len(trig))
    max_uses = s.get("max_uses")
    row = {
        "id": sid,
        "name_key": s.get("name_key"),
        "name_en": s.get("name_en"),
        "type": s.get("type"),
        "sprite": s.get("sprite"),
        "mana": 10 if s.get("mana") is None else s.get("mana"),   # gun.lua ACTION_MANA_DRAIN_DEFAULT
        "max_uses": -1 if max_uses is None else max_uses,
        "price": s.get("price"),
        "spawn_level": s.get("spawn_level") or [],
        "spawn_probability": s.get("spawn_probability") or [],
        "projectiles": s.get("projectiles") or [],
        "trigger_kind": t["kind"] if t else "none",
        "trigger_file": t["file"] if t else "none",
        "trigger_draws": t["draws"] if t else 0,
        "trigger_frames": (t.get("frames") or 0) if t else 0,
        "draws": s.get("draws") or 0,
        "reload_add": s.get("reload_add") or 0,
        "config_add": s.get("config_add") or {},
        "config_mul": s.get("config_mul") or {},
        "config_set": numbers,
        "game_effects": effects or "none",
        "shot_add": s.get("shot_add") or {},
        "shot_set": s.get("shot_set") or {},
        "clamps": s.get("clamps") or [],
        "extra_entities": extra or "none",
        "port": "hand" if why else "data",
        "stage": "3",
        "_unverified": {"port": "port by hand: " + "; ".join(why)} if why else {},
        "_sources": {"all": "gun_actions.lua via tncli spells"},
    }
    if why:
        row["_sources"]["port"] = "; ".join(why)
    return row


def wand_row(path, w):
    comps = {}
    ab = w.get("ability") or {}
    comps["AbilityComponent"] = ab.get("attrs", {})
    for ch in ab.get("children", []):
        comps[ch["component"]] = ch.get("attrs", {})
    row = {"id": os.path.splitext(os.path.basename(path))[0], "entity": path}
    unv = {}
    for col, (comp, attr, kind) in WAND_ATTRS.items():
        v = comps.get(comp, {}).get(attr)
        if v is None or v == "":
            row[col] = None
            unv[col] = "not in the entity file" + (" (set by %s)" % ", ".join(w.get("scripts") or []) if w.get("scripts") else "")
            continue
        if kind is bool:
            row[col] = v.strip() in ("1", "true")
        elif kind is int:
            row[col] = int(float(v))
        elif kind is float:
            row[col] = float(v)
        else:
            row[col] = v
    row["scripts"] = w.get("scripts") or []
    row["stage"] = "3"
    row["_unverified"] = unv
    row["_sources"] = {"all": "wand entity via tncli spells"}
    return row


SPELLS_DESC = "Noita spells (stage 3), read from the player's gun_actions.lua."
WANDS_DESC = "Noita wand entities (stage 3): stats from their AbilityComponent."


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("facts", nargs="?")
    ap.add_argument("--empty", action="store_true", help="write the schemas only, keeping existing rows")
    a = ap.parse_args()
    if a.empty or not a.facts:
        write("spells", SPELLS_DESC, SPELL_COLUMNS, (load("spells") or {}).get("rows", []))
        write("wands", WANDS_DESC, WAND_COLUMNS, (load("wands") or {}).get("rows", []))
        print("schemas written")
        return
    with open(a.facts, encoding="utf-8") as f:
        facts = json.load(f)
    spells = [spell_row(k, v) for k, v in facts.get("spells", {}).items()]
    wands = [wand_row(k, v) for k, v in facts.get("wands", {}).items() if "error" not in v]
    write("spells", SPELLS_DESC, SPELL_COLUMNS, spells)
    write("wands", WANDS_DESC, WAND_COLUMNS, wands)
    shots = [shot_row(k, v) for k, v in sorted(facts.get("projectiles", {}).items()) if "error" not in v]
    write("spell_projectiles", SHOTS_DESC, SHOT_COLUMNS, shots)
    print("spell projectiles: %d (%d without a ProjectileComponent)" % (len(shots), sum(1 for r in shots if r["_unverified"])))
    hand = sum(1 for r in spells if r["port"] == "hand")
    print("spells: %d (%d by data, %d by hand); wands: %d" % (len(spells), len(spells) - hand, hand, len(wands)))


if __name__ == "__main__":
    main()
