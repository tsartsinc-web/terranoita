"""Noita liquids and gases -> design/sheets/liquids.json and design/sheets/reactions.json (stage 2).

  tncli wak-cat <noita> data/materials.xml > build/materials.xml      (PC only: needs the player's Noita)
  python tools/extract_liquids.py build/materials.xml

Everything is read from Noita's materials.xml: every material whose cell_type is liquid without liquid_sand
(liquid_sand ones are powders: materials.json) or gas, with CellDataChild inheritance resolved; and every reaction
that involves at least one of them. Terraria effects of Noita's status effects are filled by hand later
(status_effects column of liquids: Noita names; mapping to Terraria buffs is its own sheet).
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS = os.path.join(ROOT, "design", "sheets")

KEEP = ["cell_type", "density", "liquid_gravity", "liquid_viscosity", "liquid_stains", "liquid_slime", "burnable",
        "on_fire", "fire_hp", "autoignition_temperature", "temperature_of_fire", "generates_smoke", "generates_flames",
        "requires_oxygen", "lifetime", "gas_upwards_speed", "gas_horizontal_speed", "gas_downwards_speed",
        "status_effects", "cold_freezes_to_material", "warmth_melts_to_material", "always_ignites_damagemodel",
        "danger_fire", "danger_poison", "danger_radioactive", "danger_water", "hp", "tags", "wang_color", "ui_name",
        "electrical_conductivity", "gfx_glow", "liquid_sand"]


def load(path):
    text = open(path, encoding="utf-8", errors="replace").read()
    text = re.sub(r"<\?xml[^>]*\?>", "", text)
    root = ET.fromstring("<Root>" + text + "</Root>")
    mats = {}
    for el in root.iter():
        if el.tag not in ("CellData", "CellDataChild"):
            continue
        a = dict(el.attrib)
        g = el.find("Graphics")
        if g is not None and g.get("color"):
            a["graphics_color"] = g.get("color")
        ing = el.find("StatusEffects/Ingestion")
        if ing is not None:
            a["ingestion"] = [(s.get("type"), float(s.get("amount", "0"))) for s in ing.findall("StatusEffect")]
        stains = el.find("StatusEffects/Stains")
        if stains is not None:
            a["stains"] = [s.get("type") for s in stains.findall("StatusEffect")]
        a["_child"] = el.tag == "CellDataChild"
        mats[a["name"]] = a
    # inheritance: a child takes its parent's attributes unless it sets them
    def resolved(name, seen=()):
        a = mats[name]
        p = a.get("_parent")
        if not p or p not in mats or p in seen:
            return a
        base = dict(resolved(p, seen + (name,)))
        base.update({k: v for k, v in a.items() if k != "_parent"})
        return base
    return {n: resolved(n) for n in mats}, root


def num(v, default=0.0):
    try:
        return float(v)
    except (TypeError, ValueError):
        return default


def color(a):
    c = (a.get("graphics_color") or a.get("wang_color") or "ff808080").lower()
    return c if re.fullmatch(r"[0-9a-f]{8}", c) else "ff808080"


# author: freezing liquid (and its vapour) slows whoever touches it; in Noita it only acts when drunk
AUTHOR_TOUCH = {"blood_cold": ["CHILLED"], "blood_cold_vapour": ["CHILLED"]}

TILE_BY_NAME = [  # first match wins
    ("lavarock", "Obsidian"), ("obsidian", "Obsidian"), ("concrete", "GrayBrick"), ("glass", "Glass"),
    ("ice", "IceBlock"), ("snow", "SnowBlock"), ("mud", "Mud"), ("soil", "Dirt"), ("earth", "Dirt"),
    ("sand", "Sand"), ("salt", "Sand"), ("gold", "Gold"), ("wood", "WoodBlock"), ("fung", "MushroomBlock"),
    ("meat", "FleshBlock"), ("coal", "Ash"), ("ash", "Ash"), ("brick", "GrayBrick"), ("rock", "Stone"),
    ("stone", "Stone"), ("grass", "Grass"), ("plant", "LeafBlock"), ("steel", "IronBrick"), ("metal", "IronBrick"),
    ("silver", "Silver"), ("copper", "Copper"), ("brass", "Copper"),
]


def terraria_tile(name):
    for key, tile in TILE_BY_NAME:
        if key in name:
            return tile
    return "none"


def touch(n, a):
    """Touch effects: status_effects + stains (deduplicated); burning liquids (on_fire, e.g. Noita's "fire", oil's
    child) set you on fire and do not leave the parent's fire-proof stain; AUTHOR_TOUCH adds the author's ones."""
    out = []
    for e in [s for s in (a.get("status_effects") or "").split(",") if s] + a.get("stains", []):
        if e not in out:
            out.append(e)
    if a.get("on_fire") == "1":
        out = ["ON_FIRE"] + [e for e in out if e not in ("OILED", "WET", "BLOODY", "SLIMY", "RADIOACTIVE")]
    for e in AUTHOR_TOUCH.get(n, []):
        if e not in out:
            out.append(e)
    return out


def main():
    mats, root = load(sys.argv[1])
    rows = []
    for n, a in sorted(mats.items()):
        kind = a.get("cell_type")
        if kind == "liquid" and a.get("liquid_sand", "0") == "1":
            continue
        if kind not in ("liquid", "gas"):
            continue
        tags = [t.strip("[]") for t in (a.get("tags") or "").split(",") if t.strip()]
        rows.append({
            "id": n,
            "name_key": (a.get("ui_name") or "").lstrip("$"),
            "kind": kind,
            "color": color(a),
            "density": num(a.get("density"), 1.0),
            "gravity": num(a.get("liquid_gravity"), 0.0),
            "viscosity": num(a.get("liquid_viscosity"), 0.0),
            "burnable": a.get("burnable") == "1",
            "fire_hp": num(a.get("fire_hp"), 0.0),
            "on_fire": a.get("on_fire") == "1",
            "glow": num(a.get("gfx_glow"), 0.0),
            "lifetime": num(a.get("lifetime"), 0.0),
            "touch_effects": touch(n, a),
            "ingestion": ["%s:%g" % (t, v) for t, v in a.get("ingestion", [])],
            "freezes_to": a.get("cold_freezes_to_material") or "none",
            "melts_to": a.get("warmth_melts_to_material") or "none",
            "tags": tags,
            "creative": a.get("show_in_creative_mode") == "1",
            "stage": "2",
            "_unverified": {},
            "_sources": {"all": "materials.xml " + ("CellDataChild of " + a["_parent"] if a.get("_parent") else "CellData")},
        })
    names = {r["id"] for r in rows}
    reactions = []
    for el in root.iter("Reaction"):
        a = el.attrib
        cells = [a.get(k, "") for k in ("input_cell1", "input_cell2", "output_cell1", "output_cell2")]
        if not any(c in names for c in cells):
            continue
        reactions.append({
            "id": "r%03d" % len(reactions),
            "probability": num(a.get("probability")),
            "input1": cells[0], "input2": cells[1], "output1": cells[2], "output2": cells[3],
            "fast": el.tag == "ReactionFast" or a.get("fast_reaction") == "1",
            "explosion": num(a.get("explosion_power") or (el.find("ExplosionConfig").get("explosion_radius")
                                                           if el.find("ExplosionConfig") is not None else 0)),
            "stage": "2", "_unverified": {}, "_sources": {"all": "materials.xml Reaction"},
        })

    # solids the reactions and the block physics need: their tags (for [tag] inputs) and the Terraria tile a
    # reaction that makes them leaves behind
    mats_sheet = json.load(open(os.path.join(SHEETS, "materials.json"), encoding="utf-8"))
    wanted = {"rock_static"} | {r["noita_material"] for r in mats_sheet["rows"]}
    for r in reactions:
        for c in (r["input1"], r["input2"], r["output1"], r["output2"]):
            if c and not c.startswith("[") and c in mats and c not in names and c != "air":
                wanted.add(c)
    solids = []
    for n in sorted(wanted):
        if n not in mats:
            continue
        a = mats[n]
        solids.append({"id": n, "cell_type": a.get("cell_type", "solid"),
                       "tags": [t.strip("[]") for t in (a.get("tags") or "").split(",") if t.strip()],
                       "terraria_tile": terraria_tile(n), "stage": "2", "_unverified": {},
                       "_sources": {"all": "materials.xml; terraria_tile by name (tools/extract_liquids.py terraria_tile)"}})

    liquid_cols = {
        "id": {"type": "string", "desc": "Noita material name."},
        "name_key": {"type": "string", "desc": "Translation key in Noita's common.csv."},
        "kind": {"type": "enum", "values": ["liquid", "gas"], "desc": "Noita cell_type."},
        "color": {"type": "string", "desc": "ARGB hex (Graphics color, else wang_color)."},
        "density": {"type": "number", "desc": "Heavier sinks under lighter (Noita density)."},
        "gravity": {"type": "number", "desc": "Noita liquid_gravity."},
        "viscosity": {"type": "number", "desc": "Noita liquid_viscosity (0 = runny)."},
        "burnable": {"type": "bool", "desc": "Catches fire."},
        "fire_hp": {"type": "number", "desc": "How long it burns (Noita fire_hp)."},
        "on_fire": {"type": "bool", "desc": "Is itself burning (fire materials)."},
        "glow": {"type": "number", "desc": "Noita gfx_glow (0 = none)."},
        "lifetime": {"type": "number", "desc": "Gas lifetime in Noita frames (0 = forever)."},
        "touch_effects": {"type": "string[]", "desc": "Noita status effects on touch (status_effects + stains)."},
        "ingestion": {"type": "string[]", "desc": "Noita status effects when drunk, TYPE:amount."},
        "freezes_to": {"type": "string", "desc": "Material it freezes into ('none')."},
        "melts_to": {"type": "string", "desc": "Material it melts into ('none')."},
        "tags": {"type": "string[]", "desc": "Noita tags."},
        "creative": {"type": "bool", "desc": "Shown in Noita's creative mode (a 'real' material, not an effect)."},
        "stage": {"type": "enum", "values": ["1a", "1b", "1c", "2", "3", "4"], "desc": "Stage that builds it."},
    }
    reaction_cols = {
        "id": {"type": "string", "desc": "Row id."},
        "probability": {"type": "number", "desc": "Noita probability (per contact and frame, of 100)."},
        "input1": {"type": "string", "desc": "Material or [tag]."},
        "input2": {"type": "string", "desc": "Material or [tag]."},
        "output1": {"type": "string", "desc": "Material input1 turns into."},
        "output2": {"type": "string", "desc": "Material input2 turns into."},
        "fast": {"type": "bool", "desc": "Noita fast reaction."},
        "explosion": {"type": "number", "desc": "Explosion radius (0 = none)."},
        "stage": {"type": "enum", "values": ["1a", "1b", "1c", "2", "3", "4"], "desc": "Stage that builds it."},
    }
    solid_cols = {
        "id": {"type": "string", "desc": "Noita material name."},
        "cell_type": {"type": "string", "desc": "Noita cell_type."},
        "tags": {"type": "string[]", "desc": "Noita tags."},
        "terraria_tile": {"type": "string", "desc": "TileID a reaction leaves when it makes this ('none' = nothing)."},
        "stage": {"type": "enum", "values": ["1a", "1b", "1c", "2", "3", "4"], "desc": "Stage that builds it."},
    }
    for name, desc, cols, data in (
            ("noita_solids", "Stage 2: Noita solids and powders the reactions and block physics refer to.", solid_cols, solids),
            ("liquids", "Stage 2: Noita liquids and gases (materials.xml), simulated on top of Terraria's tiles.", liquid_cols, rows),
            ("reactions", "Stage 2: Noita reactions involving liquids and gases (materials.xml).", reaction_cols, reactions)):
        with open(os.path.join(SHEETS, name + ".json"), "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps({"sheet": name, "description": desc, "key": "id", "columns": cols, "rows": data},
                               ensure_ascii=False, indent=1) + "\n")
    print("liquids %d (%d gas), reactions %d" % (len(rows), sum(r["kind"] == "gas" for r in rows), len(reactions)))


if __name__ == "__main__":
    main()
