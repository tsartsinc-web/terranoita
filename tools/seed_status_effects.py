"""design/sheets/status_effects.json (stage 2): Noita status effects (liquids.touch_effects / ingestion) as our own
effects in Terraria: Noita's icon and name (data/scripts/status_effects/status_list.lua), and what our code does
(Terraria has no room for new buffs, so they are shown next to its buff bar and run by src/Terranoita/Physics/Status.cs).

  tncli wak-cat <noita> data/scripts/status_effects/status_list.lua > build/status_list.lua
  python tools/seed_status_effects.py build/status_list.lua
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "design", "sheets", "status_effects.json")

# id: (what our code does, seconds a touch lasts)
ROWS = {
    "WET": ("puts out fire and keeps it off", 10),
    "OILED": ("drips oil; puts out fire (Noita protects_from_fire)", 15),
    "BLOODY": ("drips blood; puts out fire", 10),
    "SLIMY": ("70% movement speed; puts out fire", 10),
    "RADIOACTIVE": ("loses 6 hp/s; puts out fire", 6),
    "ALCOHOLIC": ("stumbles (random sideways push)", 20),
    "INGESTION_DRUNK": ("stumbles (random sideways push)", 20),
    "POISONED": ("loses 2 hp/s", 5),
    "FOOD_POISONING": ("loses 2 hp/s, 80% movement speed", 8),
    "ON_FIRE": ("burns: Terraria's on-fire damage and flames; lights what it touches", 4),
    "INGESTION_ON_FIRE": ("burns: Terraria's on-fire damage and flames; lights what it touches", 4),
    "TRIP": ("sparkles of every colour around the player", 10),
    "CONFUSION": ("left and right swapped (Terraria confused)", 6),
    "WEAKNESS": ("takes double damage", 15),
    "HP_REGENERATION": ("heals 10 hp/s", 6),
    "MANA_REGENERATION": ("mana refills fast", 15),
    "MOVEMENT_FASTER_2X": ("double movement speed", 15),
    "FASTER_LEVITATION": ("holding jump lifts the player", 15),
    "NIGHTVISION": ("sees in the dark (Terraria night owl)", 30),
    "INVISIBILITY": ("invisible; Noita enemies lose sight of the player", 15),
    "PROTECTION_ALL": ("takes no damage", 15),
    "BERSERK": ("deals double damage", 15),
    "CHARM": ("Noita enemies that touch it fight for the player", 30),
    "POLYMORPH": ("cannot use items, 60% movement speed", 8),
    "POLYMORPH_RANDOM": ("cannot use items, 60% movement speed", 8),
    "POLYMORPH_UNSTABLE": ("cannot use items, 60% movement speed", 8),
    "TELEPORTATION": ("every 2 s teleports 10-25 tiles to a free spot", 6),
    "UNSTABLE_TELEPORTATION": ("every 2 s teleports 40-80 tiles to a free spot", 6),
    "WORM_ATTRACTOR": ("Noita worms hunt the player from twice as far", 30),
    "INGESTION_FREEZING": ("frozen in place (Terraria frozen)", 3),
    "CHILLED": ("half movement speed (author: freezing liquid slows on touch; Noita icon and name of freezing)", 4),
    "FARTS": ("green puffs (Terraria stinky)", 15),
    "RAINBOW_FARTS": ("rainbow puffs", 15),
    "JARATE": ("yellow drips", 15),
}

# author: only opposite effects replace each other, the rest stay together. Pairs (both ways):
OPPOSITES = [
    ("ON_FIRE", "WET"), ("ON_FIRE", "CHILLED"), ("ON_FIRE", "INGESTION_FREEZING"), ("INGESTION_ON_FIRE", "WET"),
    ("INGESTION_ON_FIRE", "CHILLED"), ("INGESTION_ON_FIRE", "INGESTION_FREEZING"),
    ("WET", "OILED"), ("WET", "BLOODY"), ("WET", "SLIMY"), ("WET", "JARATE"),   # water washes stains off
    ("MOVEMENT_FASTER_2X", "SLIMY"), ("MOVEMENT_FASTER_2X", "CHILLED"), ("MOVEMENT_FASTER_2X", "FOOD_POISONING"),
    ("HP_REGENERATION", "POISONED"), ("HP_REGENERATION", "RADIOACTIVE"), ("HP_REGENERATION", "FOOD_POISONING"),
    ("PROTECTION_ALL", "WEAKNESS"), ("BERSERK", "WEAKNESS"),
    ("POLYMORPH", "POLYMORPH_RANDOM"), ("POLYMORPH", "POLYMORPH_UNSTABLE"), ("POLYMORPH_RANDOM", "POLYMORPH_UNSTABLE"),
    ("TELEPORTATION", "UNSTABLE_TELEPORTATION"),
]

COLUMNS = {
    "id": {"type": "string", "desc": "Noita status effect id (status_list.lua)."},
    "name_key": {"type": "string", "desc": "Name in Noita's common.csv."},
    "desc_key": {"type": "string", "desc": "Description in Noita's common.csv."},
    "icon": {"type": "string", "desc": "Icon in data.wak."},
    "harmful": {"type": "bool", "desc": "Noita is_harmful."},
    "protects_from_fire": {"type": "bool", "desc": "Noita protects_from_fire: puts out and keeps off fire."},
    "removes_cause": {"type": "bool", "desc": "Noita remove_cells_that_cause_when_activated: the liquid that caused it is used up."},
    "mechanic": {"type": "string", "desc": "What our code does while it lasts."},
    "cancels": {"type": "string[]", "desc": "Opposite effects it ends when it starts (author: the rest stay together)."},
    "seconds": {"type": "number", "desc": "How long one touch lasts (refreshed while touching)."},
    "stage": {"type": "enum", "values": ["1a", "1b", "1c", "2", "3", "4"], "desc": "Stage that builds it."},
}


def parse_lua(path):
    text = open(path, encoding="utf-8", errors="replace").read()
    out = {}
    for block in re.findall(r"\{([^{}]*)\}", text):
        f = dict(re.findall(r'(\w+)\s*=\s*"([^"]*)"', block))
        f.update({k: v == "true" for k, v in re.findall(r"(\w+)\s*=\s*(true|false)", block)})
        if "id" in f:
            out[f["id"]] = f
    return out


lua = parse_lua(sys.argv[1])
liquids = json.load(open(os.path.join(ROOT, "design", "sheets", "liquids.json"), encoding="utf-8"))
used = {e.split(":")[0] for r in liquids["rows"] for e in r["touch_effects"] + r["ingestion"]}
missing = sorted(used - set(ROWS))
if missing:
    raise SystemExit("no row for: " + ", ".join(missing))
rows = []
for k, (mech, secs) in ROWS.items():
    n = lua.get(k) or lua.get(k.replace("INGESTION_", "")) or (lua.get("INGESTION_FREEZING") if k == "CHILLED" else None) or {}
    rows.append({"id": k, "name_key": n.get("ui_name", "").lstrip("$") or "none",
                 "desc_key": n.get("ui_description", "").lstrip("$") or "none",
                 "icon": n.get("ui_icon") or "none", "harmful": bool(n.get("is_harmful", False)),
                 "protects_from_fire": bool(n.get("protects_from_fire", False)),
                 "removes_cause": bool(n.get("remove_cells_that_cause_when_activated", False)),
                 "cancels": sorted({b for a, b in OPPOSITES if a == k} | {a for a, b in OPPOSITES if b == k}), "mechanic": mech, "seconds": secs,
                 "stage": "2", "_unverified": {},
                 "_sources": {"all": "status_list.lua" + ("" if lua.get(k) else " (" + (k.replace("INGESTION_", "") if n else "not listed") + ")")}})
with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write(json.dumps({"sheet": "status_effects", "description": "Stage 2: Noita status effects, run by our code with Noita's icons.",
                        "key": "id", "columns": COLUMNS, "rows": rows}, ensure_ascii=False, indent=1) + "\n")
print("wrote", len(rows), "rows;", sum(r["icon"] != "none" for r in rows), "with icons")
