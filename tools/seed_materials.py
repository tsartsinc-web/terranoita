"""One-off: create design/sheets/materials.json (stage 2 block physics). Numbers come from the author's Noita
data/materials.xml (read with `tncli wak-cat <noita> data/materials.xml`): which materials are powders
(liquid_sand, not static), which burn (burnable, fire_hp), which melt (reactions with [fire])."""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "design", "sheets", "materials.json")

COLUMNS = {
    "id": {"type": "string", "desc": "Material group id."},
    "terraria_tiles": {"type": "string[]", "desc": "Terraria TileID names in this group."},
    "noita_material": {"type": "string", "desc": "The Noita material (materials.xml) it behaves like."},
    "falls": {"type": "enum", "values": ["none", "powder", "weightless"],
              "desc": "powder: once disturbed, falls when nothing is under it (Noita liquid_sand). weightless: never falls,"
                      " even when the player placed it, and holds whatever is attached to it. none: stays (Noita static),"
                      " unless the player placed it (then it needs support, see systems block_physics)."},
    "falls_as": {"type": "string", "desc": "TileID it lands as ('same' = the same tile; grass lands as its soil)."},
    "burns": {"type": "bool", "desc": "Catches fire (Noita burnable=1)."},
    "burn_seconds": {"type": "number", "desc": "How long one tile burns: Noita fire_hp / 100 (0 = does not burn)."},
    "burns_to": {"type": "string", "desc": "TileID left after burning ('none' = nothing)."},
    "melts_to": {"type": "enum", "values": ["none", "water"], "desc": "Next to fire or lava it turns into this liquid."},
    "stage": {"type": "enum", "values": ["1a", "1b", "1c", "2", "3", "4"], "desc": "Stage that builds it."},
}

WOOD = ["WoodBlock", "BorealWood", "PalmWood", "RichMahogany", "Ebonwood", "Shadewood", "Pearlwood", "DynastyWood",
        "SpookyWood", "AshWood", "PineWoodBlock", "LivingWood", "LivingMahogany", "BambooBlock", "LargeBambooBlock",
        "Platforms", "WoodenBeam", "BorealBeam", "RichMahoganyBeam", "HayBlock", "Rope", "Cobweb"]
TREES = ["Trees", "PalmTree", "PineTree", "TreeAsh", "Bamboo"]
PLANTS = ["LeafBlock", "LivingMahoganyLeaves", "Vines", "JungleVines", "CrimsonVines", "CorruptVines", "HallowedVines",
          "AshVines", "VineFlowers"]

src_powder = "materials.xml: cell_type liquid, liquid_sand 1, no liquid_static (falls and piles)"
ROWS = [
    ("soil", ["Dirt", "ClayBlock"], "soil", "powder", "", False, 0, "none", "none", src_powder),
    ("mud", ["Mud"], "mud", "powder", "", False, 0, "none", "none", src_powder),
    ("ash", ["Ash"], "sand", "powder", "", False, 0, "none", "none", src_powder + " (Noita has no ash block: sand)"),
    ("silt", ["Silt", "Slush"], "sand", "powder", "", False, 0, "none", "none", src_powder),
    ("snow", ["SnowBlock"], "snow", "powder", "", False, 0, "none", "water",
     src_powder + "; reactions [fire]+snow -> water (80), steam (20)"),
    ("grass", ["Grass", "CorruptGrass", "CrimsonGrass", "HallowedGrass", "GolfGrass", "GolfGrassHallowed"], "grass",
     "powder", "Dirt", True, 1.0, "Dirt", "none", "grass: burnable 1, fire_hp 100; grows on soil"),
    ("jungle_grass", ["JungleGrass", "CorruptJungleGrass", "CrimsonJungleGrass"], "grass", "powder", "Mud", True, 1.0,
     "Mud", "none", "grass: burnable 1, fire_hp 100; grows on mud"),
    ("mushroom_grass", ["MushroomGrass"], "fungi", "powder", "Mud", True, 0.6, "Mud", "none",
     "fungi: burnable 1, fire_hp 60"),
    ("ash_grass", ["AshGrass"], "grass", "powder", "Ash", True, 1.0, "Ash", "none", "grass: burnable 1, fire_hp 100"),
    ("wood", WOOD, "wood", "none", "", True, 6.0, "none", "none",
     "wood / wood_static / wood_player: burnable 1, fire_hp 600"),
    ("trees", TREES, "wood", "none", "", True, 6.0, "none", "none", "wood: burnable 1, fire_hp 600"),
    ("mushroom_trees", ["MushroomTrees", "MushroomVines", "MushroomBlock"], "fungi", "none", "", True, 0.6, "none",
     "none", "fungi: burnable 1, fire_hp 60"),
    ("plants", PLANTS, "plant_material", "none", "", True, 1.0, "none", "none",
     "plant_material: burnable 1, no fire_hp (grass's 100 used)"),
    ("ice", ["IceBlock", "CorruptIce", "HallowedIce", "FleshIce", "BreakableIce"], "ice", "none", "", False, 0, "none",
     "water", "reaction [fire]+ice -> water (40); Terraria ice stays put (author: only loose blocks fall)"),
    ("clouds", ["Cloud", "RainCloud", "SnowCloud"], "-", "weightless", "", False, 0, "none", "none",
     "author: clouds are weightless"),
]

# background walls of the same stuff: they burn like it (burned walls are gone)
WALLS = {
    "wood": ["Wood", "Planked", "Ebonwood", "Pearlwood", "LivingWood", "LivingWoodUnsafe", "Shadewood", "WoodenFence",
             "SpookyWood", "EbonwoodFence", "RichMahoganyFence", "PearlwoodFence", "ShadewoodFence", "WhiteDynasty",
             "BlueDynasty", "BorealWood", "BorealWoodFence", "PalmWood", "PalmWoodFence", "BambooBlockWall",
             "LargeBambooBlockWall", "BambooFence", "AshWood", "AshWoodFence", "FeywoodWall", "PineWoodBlockWall",
             "RichMaogany", "Hay"],
    "grass": ["Grass", "GrassUnsafe", "Flower", "FlowerUnsafe", "CorruptGrassUnsafe", "HallowedGrassUnsafe",
              "CrimsonGrassUnsafe"],
    "jungle_grass": ["JungleUnsafe", "Jungle"],
    "mushroom_trees": ["Mushroom", "MushroomUnsafe"],
    "plants": ["LivingLeaf"],
}
COLUMNS = dict(list(COLUMNS.items())[:2] + [("terraria_walls", {"type": "string[]", "desc":
           "Terraria WallID names of the same stuff: background walls burn like it (and are gone)."})] +
           list(COLUMNS.items())[2:])

rows = []
for (rid, tiles, mat, falls, falls_as, burns, secs, burns_to, melts, src) in ROWS:
    rows.append({"id": rid, "terraria_tiles": tiles, "terraria_walls": WALLS.get(rid, []), "noita_material": mat,
                 "falls": falls, "falls_as": falls_as or "same",
                 "burns": burns, "burn_seconds": secs, "burns_to": burns_to, "melts_to": melts, "stage": "2",
                 "_unverified": {}, "_sources": {"all": src + ". Which tiles fall: author (loose ones only)."}})
sheet = {"sheet": "materials", "description": "Stage 2 block physics: how Terraria tiles behave as Noita materials.",
         "key": "id", "columns": COLUMNS, "rows": rows}
with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write(json.dumps(sheet, ensure_ascii=False, indent=1) + "\n")
print("wrote", OUT, len(rows), "rows")
