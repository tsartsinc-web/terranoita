"""design/sheets/biome_spawns.json: how often the worldgen calls each Noita biome's placement functions
(design/worldgen_plan.md section 2). Simple numbers the author tunes, per 10 000 Terraria tiles of the biome's zone;
the functions themselves are Noita's (data/scripts/biomes/<script>.lua, run by Core NoitaBiomeSpawns).

Rows exist for every biome of biome_map.json that has a zone. Script names are Noita's file names as far as known;
each is `_unverified` until the PC lists data/scripts/biomes (`tncli wak-list <noita> data/scripts/biomes`) and
`tncli biome-spawns` runs it. Existing rows keep the author's numbers when the tool runs again.

Usage: python tools/seed_biome_spawns.py
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS = os.path.join(ROOT, "design", "sheets")

# Noita biome (biome_map.json id) -> its script in data/scripts/biomes (best known names; checked on the PC)
# biome -> its Noita script: data/biome/<x>.xml lua_script, English names from data/translations/common.csv
# (checked on the PC 2026-10-09 with tncli wak-cat)
SCRIPTS = {
    "Forest": "hills", "Lake": "lake", "Mines": "coalmine", "Collapsed Mines": "coalmine_alt", "Desert": "desert",
    "Snowy Wasteland": "winter", "Coal Pits": "excavationsite", "Snowy Depths": "snowcave", "Hiisi Base": "snowcastle",
    "Underground Jungle": "rainforest", "Fungal Caverns": "fungicave", "Sandcave": "sandcave",
    "Magical Temple": "wandcave", "Ancient Laboratory": "liquidcave", "Pyramid": "pyramid", "Desert Chasm": "desert",
    "The Vault": "vault", "Frozen Vault": "vault_frozen", "Snowy Chasm": "winter", "Temple of the Art": "crypt",
    "Wizards' Den": "wizardcave", "Overgrown Cavern": "fungiforest", "Lukki Lair": "rainforest_dark",
    "Meat Realm": "meat", "Cloudscape": "clouds", "Power Plant": "robobase", "The Work (Hell)": "the_end",
    "The Work (Sky)": "the_end",
}
# not a Noita biome name (no biome_* text): the nearest script, to check by eye
GUESSED = {"Desert Chasm": "no biome_* name in common.csv; desert.lua assumed"}

# function -> default calls per 10 000 tiles (author tunes)
# (no spawn_pixel_scenes: not a Noita function; scenes come from spawn_items and the scripts' load_* functions)
FUNCTIONS = {"spawn_wands": 1.0, "spawn_potions": 2.0, "spawn_items": 1.5, "spawn_chest": 0.5}
# what each function does in each script on the player's Noita (tncli biome-spawns on the PC): an "error" there means
# the script lacks the function or its table (g_items...), so Noita never calls it in that biome -> 0
PROBE = os.path.join(os.path.dirname(SHEETS), "sources", "pc_biome_functions.json")

COLUMNS = {
    "id": {"type": "string", "desc": "<biome>:<function>."},
    "biome": {"type": "string", "desc": "Noita biome (biome_map.json id)."},
    "script": {"type": "string", "desc": "Noita script, data/scripts/biomes/<script>.lua."},
    "function": {"type": "string", "desc": "The script's placement function the worldgen calls."},
    "per_10k_tiles": {"type": "number", "desc": "Calls per 10 000 tiles of the biome's Terraria zone (author tunes; 0 = off)."},
    "stage": {"type": "enum", "values": ["1a", "1b", "1c", "2", "3", "4"], "desc": "Stage that builds it (worldgen: 4)."},
}


_probe = None


def probe():
    global _probe
    if _probe is None:
        _probe = {}
        if os.path.exists(PROBE):
            with open(PROBE, encoding="utf-8") as f:
                _probe = json.load(f)["scripts"]
    return _probe


def absent(script, fn):
    return str(probe().get(script or "", {}).get(fn, "")).startswith("error")


def rows(biomes, old):
    out = []
    for b in biomes:
        if not b.get("zone") or b["zone"] == "none":
            continue
        script = SCRIPTS.get(b["id"])
        for fn, n in FUNCTIONS.items():
            rid = b["id"] + ":" + fn
            prev = old.get(rid, {})
            out.append({
                "id": rid, "biome": b["id"], "script": script or "none", "function": fn,
                "per_10k_tiles": 0 if absent(script, fn) else prev.get("per_10k_tiles", n), "stage": "4",
                "_unverified": {"script": GUESSED[b["id"]]} if b["id"] in GUESSED else {} if script else {"script": "no known script"},
                "_sources": {"all": "tools/seed_biome_spawns.py defaults; numbers: author",
                             **({"per_10k_tiles": "0: " + probe()[script][fn] + " (pc_biome_functions.json)"} if absent(script, fn) else {})},
            })
    return out


def main():
    with open(os.path.join(SHEETS, "biome_map.json"), encoding="utf-8") as f:
        biomes = json.load(f)["rows"]
    path = os.path.join(SHEETS, "biome_spawns.json")
    old = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            old = {r["id"]: r for r in json.load(f)["rows"]}
    out = rows(biomes, old)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"sheet": "biome_spawns", "description": "Worldgen: how often Noita's biome placement functions run per "
                   "Terraria zone (design/worldgen_plan.md).", "key": "id", "columns": COLUMNS, "rows": out},
                  f, ensure_ascii=False, indent=1)
        f.write("\n")
    print("biome_spawns: %d rows" % len(out))


if __name__ == "__main__":
    main()
