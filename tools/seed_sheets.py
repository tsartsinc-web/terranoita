"""One-time seed of the stage-1 design sheets from the Noita wiki creature table.

The sheets in design/sheets are the source of truth once seeded: edit them by hand,
not this script. Re-running refuses to overwrite existing sheets unless --force.

Every value the wiki cannot give (file paths inside data.wak, timings, hitboxes) is
left null (unfilled) or marked in the row's "_unverified" map with how to verify it,
so tools/preflight.py lists it until someone checks it against the player's Noita.
"""
import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS = os.path.join(ROOT, "design", "sheets")
SOURCE = os.path.join(ROOT, "design", "sources", "noita_wiki_creatures.json")

WAK = "check in the player's data.wak (tools: Terranoita.Cli wak-cat)"

# --- Stage 1a vertical slice: early enemies covering the main archetypes. ---
SLICE_1A = [
    "zombie_weak", "rat", "shotgunner_weak", "miner_weak", "acidshooter_weak",
    "slimeshooter_weak", "firemage_weak", "fireskull", "fly", "longleg", "frog", "bat",
]

# Rows the wiki lists without an entity id get a stable synthetic id.
SYNTHETIC_IDS = {
    ("Amppari Hive", ""): "nest_fly",
    ("Hämis Nest", ""): "nest_longleg",
    ("Firefly Hive", ""): "nest_firebug",
    ("Death Orb", "Ancient Laboratory,Temple of the Art"): "death_orb_lab",
    ("Death Orb", "Throne Room"): "death_orb_throne",
    ("Houre Crystal", ""): "ghost_crystal",
    ("Acid Trap", ""): "trap_acid",
    ("Arrow Trap", ""): "trap_arrow",
    ("Fire Trap", ""): "trap_fire",
    ("Thunder Trap", ""): "trap_thunder",
    ("Blood Orb", ""): "blood_orb",
    ("Innocent Spirit", ""): "wisp",
}

# --- AI archetype per enemy (hand-curated from the wiki pages and Noita play). ---
ARCHETYPE_GROUPS = {
    "walker_melee": ["zombie_weak", "zombie", "rat", "wolf", "skullrat", "plague_rats_rat", "assassin",
                     "failed_alchemist", "statue", "ultimate_killer", "friend", "friend_hostile",
                     "fungus", "fungus_big", "fungus_tiny", "fungus_giga", "bigzombie", "bigzombietorso",
                     "bigzombiehead", "ant"],
    "walker_shooter": ["shotgunner_weak", "shotgunner", "shotgunner_hell", "scavenger_smg", "sniper",
                       "sniper_hell", "scavenger_glue", "scavenger_poison", "roboguard", "roboguard_big",
                       "monk", "soldier", "hidden", "necrobot", "necrobot_super", "cook", "coward",
                       "flamer", "icer", "scavenger_heal", "scavenger_shield", "scavenger_invis", "ghoul"],
    "walker_thrower": ["miner_weak", "miner", "miner_hell", "miner_santa", "miner_fire", "goblin_bomb",
                       "scavenger_grenade", "scavenger_clusterbomb", "scavenger_mine", "alchemist", "giant",
                       "scavenger_leader"],
    "mage_levitate": ["firemage_weak", "firemage", "icemage", "thundermage", "thundermage_big", "necromancer",
                      "necromancer_shop", "necromancer_super", "barfer", "enlightened_alchemist",
                      "failed_alchemist_b", "playerghost", "shaman"],
    "floater_caster": ["wizard_hearty", "wizard_homing", "wizard_neutral", "wizard_poly", "wizard_returner",
                       "wizard_twitchy", "wizard_tele", "wizard_dark", "wizard_weaken", "wizard_swapper",
                       "wand_ghost", "wand_ghost_charmed", "skullfly", "phantom_a", "phantom_b", "thunderskull",
                       "lasershooter", "gazer", "spitmonster", "skygazer", "statue_physics", "minipit",
                       "boss_centipede_minion"],
    "ghost_phase": ["weakspirit", "slimespirit", "confusespirit", "berserkspirit", "ghost", "ethereal_being",
                    "darkghost", "wraith", "wraith_glowing", "wraith_storm", "lurker", "boss_ghost_polyp", "wisp"],
    "flyer_lunge": ["fly", "bat", "bigbat", "firebug", "bigfirebug", "fireskull", "iceskull", "sheep_bat",
                    "sheep_fly"],
    "drone_flyer": ["drone_physics", "drone_lasership", "healerdrone_physics", "drone_shield", "spearbot"],
    "hopper": ["frog", "frog_big", "blob", "miniblob", "missilecrab", "longleg"],
    "slime_crawler": ["acidshooter", "acidshooter_weak", "slimeshooter", "slimeshooter_weak", "giantshooter",
                      "giantshooter_weak", "slimeshooter_boss_limbs", "maggot", "tentacler", "tentacler_small",
                      "bloom"],
    "wall_climber": ["lukki", "lukki_longleg", "lukki_tiny", "lukki_dark", "lukki_creepy_long"],
    "worm": ["worm", "worm_tiny", "worm_big", "worm_end", "worm_skull", "meatmaggot", "eel"],
    "swimmer": ["fish", "fish_large"],
    "helpless_walker": ["duck", "sheep", "deer", "elk", "scorpion", "player"],
    "tank": ["tank", "tank_rocket", "tank_super"],
    "static_turret": ["turret", "sentry", "neutralizer", "shooterflower", "crystal_physics", "skycrystal_physics",
                      "bloodcrystal_physics", "hpcrystal", "snowcrystal", "ghost_crystal", "trap_acid",
                      "trap_arrow", "trap_fire", "trap_thunder", "death_orb_lab", "death_orb_throne", "blood_orb",
                      "pebble"],
    "spawner_nest": ["nest_fly", "nest_longleg", "nest_firebug"],
    "mimic": ["chest_mimic", "chest_leggy", "mimic_potion", "dark_alchemist", "shaman_wind"],
}
BOSSES = ["boss_centipede", "boss_limbs", "boss_robot", "boss_meat", "boss_dragon", "boss_wizard",
          "boss_alchemist", "boss_ghost", "boss_pit", "boss_sky", "maggot_tiny", "fish_giga", "islandspirit",
          "gate_monster_a", "gate_monster_b", "gate_monster_c", "gate_monster_d", "parallel_tentacles",
          "parallel_alchemist"]

ARCHETYPES = {
    # id: (move, speed, accel, jump, gravity, sight, keep_distance, description)
    "walker_melee": ("ground", 2.2, 0.12, 6.0, True, 30, 0, "Walks/jumps toward the target, lunges and bites at close range."),
    "walker_shooter": ("ground", 1.6, 0.10, 6.0, True, 40, 10, "Hiisi-style: walks to a firing distance, shoots, backs off when too close."),
    "walker_thrower": ("ground", 1.6, 0.10, 6.0, True, 35, 8, "Walks to throwing distance and lobs arcing explosives (TNT, grenades, flasks)."),
    "mage_levitate": ("ground_levitate", 1.4, 0.08, 4.0, True, 45, 12, "Noita mage: walks, levitates up ledges and gaps, casts from range."),
    "floater_caster": ("hover", 1.2, 0.05, 0.0, False, 45, 12, "Hovers without gravity, blocked by tiles, keeps distance and casts."),
    "ghost_phase": ("phase", 1.0, 0.04, 0.0, False, 50, 0, "Drifts through tiles toward the target; curse aura or contact damage."),
    "flyer_lunge": ("fly", 3.0, 0.15, 0.0, False, 30, 0, "Erratic flyer that dashes into the target."),
    "drone_flyer": ("fly", 2.0, 0.08, 0.0, False, 40, 10, "Robot drone: flies to a standoff point and fires."),
    "hopper": ("hop", 2.5, 0.20, 7.5, True, 25, 0, "Jumps toward the target in arcs (frogs, blobs, small spiders)."),
    "slime_crawler": ("ground", 0.8, 0.06, 3.0, True, 30, 6, "Slow crawler that spits projectiles from medium range."),
    "wall_climber": ("climb", 1.8, 0.10, 0.0, False, 35, 0, "Lukki: walks on any solid surface including walls and ceilings."),
    "worm": ("burrow", 3.5, 0.12, 0.0, False, 50, 0, "Segmented worm that moves through tiles and bites."),
    "swimmer": ("swim", 1.5, 0.10, 0.0, False, 15, 0, "Swims in liquid, flops on land. Harmless."),
    "helpless_walker": ("ground", 1.2, 0.08, 5.0, True, 15, 0, "Wanders and flees. Harmless."),
    "tank": ("ground", 1.0, 0.05, 0.0, True, 45, 12, "Heavy tracked robot: rolls and fires its weapon."),
    "static_turret": ("static", 0.0, 0.0, 0.0, False, 40, 0, "Does not move; fires or radiates when the target is in sight."),
    "spawner_nest": ("static", 0.0, 0.0, 0.0, False, 30, 0, "Stationary nest that releases its creatures when the target is near."),
    "mimic": ("ground", 2.0, 0.12, 6.0, True, 10, 0, "Looks like an item or chest until the target is close, then attacks."),
}

# Noita location -> (terraria zone, tier, note)
BIOME_MAP = {
    "Forest": ("surface_forest", "t1", "The surface around the mountain."),
    "Lake": ("surface_water", "t1", "Animals and fish of the lake: any surface water."),
    "Mines": ("underground_dirt", "t1", "First Noita biome = first Terraria layer."),
    "Collapsed Mines": ("underground_dirt", "t1", ""),
    "Desert": ("surface_desert", "t1", ""),
    "Snowy Wasteland": ("surface_snow", "t1", ""),
    "Coal Pits": ("cavern", "t2", "Noita's second biome = the cavern layer."),
    "Snowy Depths": ("underground_snow", "t2", ""),
    "Hiisi Base": ("cavern_deep", "t2", "Hiisi military base = lower cavern layer."),
    "Underground Jungle": ("underground_jungle", "t2", ""),
    "Fungal Caverns": ("glowing_mushroom", "t2", ""),
    "Sandcave": ("underground_desert", "t2", ""),
    "Magical Temple": ("marble", "t2", "Ghosts and wands in marble caves."),
    "Ancient Laboratory": ("granite", "t2", "Alchemists in granite caves."),
    "Pyramid": ("underground_desert_hm", "t3", "Hardmode underground desert."),
    "Desert Chasm": ("underground_desert_hm", "t3", ""),
    "The Vault": ("cavern_hm", "t3", "Hardmode cavern layer."),
    "Frozen Vault": ("underground_snow_hm", "t3", ""),
    "Snowy Chasm": ("underground_snow_hm", "t3", ""),
    "Temple of the Art": ("dungeon", "t3", "After Skeletron, inside the Dungeon."),
    "Wizards' Den": ("underground_hallow", "t3", ""),
    "Overgrown Cavern": ("underground_jungle_hm", "t3", ""),
    "Lukki Lair": ("spider_cave_hm", "t3", "Spider caves, hardmode."),
    "Meat Realm": ("crimson_underground_hm", "t3", "Underground Crimson/Corruption, hardmode."),
    "Cloudscape": ("sky_hm", "t3", "Space layer in hardmode; floating islands themselves stay safe."),
    "Power Plant": ("lihzahrd_temple", "t4", "Robots guard the Jungle Temple."),
    "The Work (Hell)": ("underworld_post_plantera", "t4", ""),
    "The Work (Sky)": ("sky_post_plantera", "t4", ""),
    "Holy Mountain": ("none", "t1", "Safe zone in Noita; its guards come from the special rule, not natural spawns."),
    "The Tower": ("none", "t4", "Noita challenge area; no Terraria equivalent."),
}
for special in ["Throne Room", "Friend Room", "Parallel Worlds", "Forgotten Cave", "Kivi Temple", "The Laboratory",
                "Buried skull", "Treasure Chest", "Lava Lake", "Watchtower", "Dragoncave", "Abandoned Alchemy Lab"]:
    BIOME_MAP[special] = ("none", "t4", "Boss or secret location; its creature comes from a boss/special rule.")

ZONES = {
    # zone: (condition in plain words, hardmode, after_boss)
    "surface_forest": ("Overworld height, no other surface biome", False, "none"),
    "surface_water": ("Overworld height, NPC spawn tile in water", False, "none"),
    "surface_desert": ("Overworld height, desert", False, "none"),
    "surface_snow": ("Overworld height, snow", False, "none"),
    "underground_dirt": ("Dirt layer height, no special biome", False, "none"),
    "cavern": ("Rock layer height, no special biome", False, "none"),
    "cavern_deep": ("Lower half of the rock layer, no special biome", False, "none"),
    "underground_snow": ("Below surface, snow", False, "none"),
    "underground_jungle": ("Below surface, jungle", False, "none"),
    "glowing_mushroom": ("Glowing mushroom biome", False, "none"),
    "underground_desert": ("Underground desert", False, "none"),
    "marble": ("Marble cave", False, "none"),
    "granite": ("Granite cave", False, "none"),
    "underground_desert_hm": ("Underground desert", True, "none"),
    "cavern_hm": ("Rock layer height, no special biome", True, "none"),
    "underground_snow_hm": ("Below surface, snow", True, "none"),
    "dungeon": ("Dungeon", False, "skeletron"),
    "underground_hallow": ("Below surface, hallow", True, "none"),
    "underground_jungle_hm": ("Below surface, jungle", True, "none"),
    "spider_cave_hm": ("Spider cave walls behind the spawn tile", True, "none"),
    "crimson_underground_hm": ("Below surface, crimson or corruption", True, "none"),
    "sky_hm": ("Sky height", True, "none"),
    "lihzahrd_temple": ("Inside the Jungle Temple", True, "plantera"),
    "underworld_post_plantera": ("Underworld height", True, "plantera"),
    "sky_post_plantera": ("Sky height", True, "plantera"),
    "none": ("Never spawns naturally", False, "none"),
}

BALANCE = {
    # tier: (meaning, hp_mult, dmg_mult, defense, spawn_weight)
    "t1": ("Pre-hardmode, start of the game", 6.0, 1.5, 2, 1.0),
    "t2": ("Pre-hardmode, deeper layers", 10.0, 2.0, 6, 1.0),
    "t3": ("Hardmode", 25.0, 3.5, 20, 0.9),
    "t4": ("After Plantera", 45.0, 5.0, 35, 0.8),
}

EFFECT_WORDS = [("acid", "acid"), ("fire", "fire"), ("flame", "fire"), ("lava", "fire"), ("ice", "ice"),
                ("freez", "ice"), ("thunder", "electric"), ("lightning", "electric"), ("electric", "electric"),
                ("poison", "poison"), ("toxic", "poison"), ("slime", "poison"), ("polymorph", "polymorph"),
                ("teleport", "teleport"), ("blind", "blind"), ("glue", "glue"), ("heal", "heal"),
                ("tnt", "explosive"), ("grenade", "explosive"), ("bomb", "explosive"), ("mine", "explosive"),
                ("rocket", "explosive"), ("missile", "explosive"), ("cocktail", "fire"), ("laser", "light"),
                ("light", "light"), ("neutraliz", "neutralize"), ("swap", "swap"), ("pollen", "poison")]

# Wiki prose -> clean projectile id (and kind when the prose is not a projectile).
ALIASES = {
    "with_poison_trail": ("poison_trail_shot", None),
    "neutralizing": ("neutralizing_shot", None),
    "twitching": ("twitching_shot", None),
    "spits_a_small_spray_of_acid": ("acid_spray", None),
    "spits_toxic_sludge": ("toxic_sludge_spit", None),
    "throws_potions": ("thrown_potion", None),
    "throws_glitter_bombs": ("glitter_bomb", None),
    "throws_lohkare_rock_spirits": ("rock_spirit", "summon"),
    "throws_rock_spirits": ("rock_spirit", "summon"),
    "shoots_player_sensitive_mines": ("proximity_mine", None),
    "retaliates_with_lightning_bolt": ("lightning_bolt", None),
    "retaliates_with_pinpoint_of_light_when_hit": ("pinpoint_of_light", None),
    "grenade_when_close_range": ("grenade", None),
    "slimeball_but_with_a_physical_toxic_sludge_trail": ("slimeball_sludge", None),
    "thunder_charge_with_copy_trail_and_a_slow_homing": ("thunder_charge_homing", None),
    "shoots_a_bright_green_that_drops_little_blobs_that_will_pop_and_deal": ("maggot_blob", None),
    "fires_shield_buff_at_allies_granting_them_a_one_off_shield": ("shield_buff", "support"),
    "shield_gun": ("shield_buff", "support"),
    "turns_other_enemies_link_status_effects_invisible_invisible": ("none", "support"),
    "turns_other_enemies_invisible": ("none", "support"),
    "creates_a_circle_of_stillness_around_itself_for_a_short_time": ("none", "aura"),
    "blood_link": ("none", "support"),
    "innocent_spirit": ("none", "aura"),
    "holy_aura": ("none", "aura"),
    "explosion": ("none", "death_explosion"),
    "summons_ghosts_periodically": ("none", "summon"),
    "random_wand": ("none", "support"),
    "wand": ("none", "support"),
    "copies_s": ("none", "support"),
    "copies": ("none", "support"),
}

DMG_TYPES = ["melee", "projectile", "slice", "explosion", "electricity", "fire", "ice", "drill",
             "radioactive", "holy", "curse", "heal"]


def slug(text):
    text = re.sub(r"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", r"\1", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = re.sub(r"\(.*?\)", "", text)
    text = re.sub(r"\bx\s*\d+(-\d+)?\b", "", text)
    text = text.lower().replace("very similar to the player spell", "")
    text = re.sub(r"projectiles?", "", text)
    text = re.sub(r"[^a-z0-9]+", "_", text).strip("_")
    return text


def num(v):
    v = v.strip().rstrip("x")
    try:
        return float(v)
    except ValueError:
        return None


def parse_damage(v):
    v = v.strip()
    if "-" in v[1:]:
        a, b = v.split("-", 1)
        if num(a) is not None and num(b) is not None:
            return [num(a), num(b)]
    return num(v)


def split_attacks(text):
    text = re.sub(r"<span.*?</span></span>", "", text)
    parts, cur = [], ""
    for frag in text.split(","):
        if cur and ("/" not in frag or frag[:1].islower()):
            cur += "," + frag
        else:
            if cur:
                parts.append(cur)
            cur = frag
    if cur:
        parts.append(cur)
    return [p.strip() for p in parts if p.strip() and p.strip().lower() != "none"]


def classify(name):
    n = name.lower()
    if "explodes" in n and "death" in n:
        return "death_explosion"
    if "lunge" in n or n.startswith("dash"):
        return "lunge"
    if n in ("melee", "bite", "tentacle", "worm bite") or "bite" in n or n.startswith("melee"):
        return "melee"
    if "curse" in n or "damage field" in n or "aura" in n:
        return "aura"
    if "summon" in n:
        return "summon"
    if "retaliat" in n:
        return "retaliate"
    if "heal" in n:
        return "heal"
    if "teleportation projectile" == n:
        return "projectile"
    if "launches itself" in n:
        return "lunge"
    return "projectile"


def default_timing(kind):
    return {"melee": (40, 1.5), "lunge": (90, 6), "projectile": (90, 25), "aura": (1, 6),
            "death_explosion": (0, 0), "summon": (600, 30), "retaliate": (0, 30), "heal": (120, 20),
            "support": (240, 20)}.get(kind, (90, 20))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()
    if os.path.exists(os.path.join(SHEETS, "enemies.json")) and not args.force:
        sys.exit("sheets already seeded; edit design/sheets by hand (or pass --force to overwrite)")

    rows = json.load(open(SOURCE, encoding="utf-8"))["rows"]
    archetype_of = {e: a for a, es in ARCHETYPE_GROUPS.items() for e in es}
    for b in BOSSES:
        archetype_of[b] = "boss_" + b

    enemies, attacks, projectiles = [], [], {}
    seen = set()
    for r in rows:
        eid, unverified = r["id"], {}
        if not eid:
            label = r["alias"] or r["name"]
            key = (label, r["spawnLocation"] if label == "Death Orb" else "")
            if label.startswith("Innocent Spirit"):
                key = ("Innocent Spirit", "")
            eid = SYNTHETIC_IDS[key]
            unverified["id"] = "synthetic id; find the real entity file name in data.wak"
        if eid in seen:
            eid = eid + "_hostile"
            unverified["id"] = "second wiki row for the same entity; confirm it is a separate entity"
        seen.add(eid)

        hp = num(r["health"]) if r["health"] else None
        locs = [l.strip() for l in r["spawnLocation"].split(",") if l.strip()]
        known = [l for l in locs if l in BIOME_MAP]
        natural = [l for l in known if BIOME_MAP[l][0] != "none"]
        if eid in BOSSES:
            stage, rule = "1c", "boss_summon"
        elif r["category"] == "Minions" or eid in ("pebble", "bigzombietorso", "bigzombiehead", "boss_ghost_polyp",
                                                   "minipit", "wand_ghost_charmed", "plague_rats_rat", "sheep_bat",
                                                   "sheep_fly", "friend_hostile"):
            stage, rule = "1c", "spawned_by_other"
        elif eid == "player":
            stage, rule = "1c", "never"
        elif r["category"] == "Mimics":
            stage, rule = "1b", "disguised"
        elif eid in ("necromancer_shop", "necromancer_super"):
            stage, rule = "1c", "holy_mountain_guard"
        elif not natural:
            stage, rule = "1c", "special_location"
        else:
            stage, rule = ("1a" if eid in SLICE_1A else "1b"), "natural"
        tiers = sorted(BIOME_MAP[l][1] for l in (natural or known)) or ["t4"]

        dmg = {}
        for t in ["Melee", "Projectile", "Slice", "Explosion", "Electricity", "Fire", "Ice", "Drill", "Radioactive", "Holy"]:
            dmg[t.lower()] = num(r["dmgMult" + t]) if r["dmgMult" + t] else None
        if any(v is None for v in dmg.values()):
            unverified["dmg_mult"] = "wiki has no multiplier for some damage types; read DamageModelComponent " + WAK

        row = {
            "id": eid,
            "name_en": r["alias"] or re.sub(r"<.*?>", "", r["name"]).strip(),
            "name_fi": re.sub(r"<.*?>", "", r["name"]).strip(),
            "name_key": "$animal_" + eid,
            "category": r["category"] or "Uncategorised",
            "faction": r["faction"] or "none",
            "noita_hp": hp,
            "noita_entity": "data/entities/animals/%s.xml" % eid,
            "sprite": None,
            "hitbox": None,
            "ai": archetype_of.get(eid),
            "attacks": [],
            "spawn_in": natural if rule == "natural" else [],
            "spawn_rule": rule,
            "tier": tiers[0],
            "dmg_mult": dmg,
            "immunities": [i for i in r["immunities"].split(",") if i] if r["immunities"] else [],
            "blood": re.sub(r"\[\[|\]\]", "", r["blood"]) or "none",
            "drops": "none" if rule in ("never",) or r["faction"] == "helpless" else ("boss_reward" if rule == "boss_summon" else "noita_gold"),
            "stage": stage,
            "_unverified": unverified,
        }
        unverified["noita_entity"] = "path guessed from the id; " + WAK
        if hp is None:
            unverified.pop("noita_hp", None)

        # attacks
        merged = {}
        for part in split_attacks(r["attackType"]):
            bits = part.split("/")
            name = bits[0].strip()
            kind = classify(name)
            key = slug(name) or kind
            if key in ALIASES:
                key, forced = ALIASES[key]
                kind = forced or kind
                if key == "none":
                    key = slug(name)
            a = merged.setdefault(key, {"name": re.sub(r"\[\[|\]\]", "", name), "kind": kind, "damage": {}, "extra": [], "text": []})
            a["text"].append(part)
            rest = [b.strip() for b in bits[1:]]
            while len(rest) >= 2 and parse_damage(rest[1]) is not None:
                dtype = rest[0].lower()
                dtype = {"dash": "melee", "melee-bite": "melee", "bite": "melee", "healing": "heal"}.get(dtype, dtype)
                a["damage"][dtype] = parse_damage(rest[1])
                rest = rest[2:]
            if rest and re.fullmatch(r"\d+F", rest[0]):
                a["extra"].append(rest[0])
        for i, (key, a) in enumerate(merged.items()):
            aid = "%s.%s" % (eid, key or i)
            cnt = re.search(r"x\s*(\d+)(?:-(\d+))?", a["name"])
            count = [int(cnt.group(1)), int(cnt.group(2) or cnt.group(1))] if cnt else [1, 1]
            proj = "none"
            if a["kind"] in ("projectile", "retaliate") or (a["kind"] == "support" and key == "shield_buff"):
                generic = key in ("", "projectile") or key.startswith("projectile")
                proj = ("%s_shot" % eid) if generic else key
                if proj not in projectiles:
                    effect = "none"
                    for w, e in EFFECT_WORDS:
                        if w in proj:
                            effect = e
                            break
                    projectiles[proj] = {
                        "id": proj,
                        "name_en": a["name"] if not generic else "%s's shot" % row["name_en"],
                        "used_by": [],
                        "noita_file": None,
                        "sprite": None,
                        "speed": None,
                        "gravity": None,
                        "lifetime_frames": None,
                        "explosion_radius": None,
                        "effect": effect,
                        "terrain": "none",
                        "stage": stage,
                        "_unverified": {"effect": "guessed from the name; confirm in the projectile's entity XML"},
                    }
                projectiles[proj]["used_by"].append(eid)
                if stage < projectiles[proj]["stage"]:
                    projectiles[proj]["stage"] = stage
            cooldown, rng = default_timing(a["kind"])
            dmg_cells = a["damage"] or ({} if a["kind"] in ("summon", "heal", "support") else None)
            arow = {
                "id": aid,
                "enemy": eid,
                "kind": a["kind"],
                "projectile": proj,
                "count": count,
                "damage": dmg_cells,
                "per_frames": a["extra"][0] if a["extra"] else "hit",
                "cooldown_frames": cooldown,
                "range_tiles": rng,
                "wiki_text": "; ".join(a["text"]),
                "stage": stage,
                "_unverified": {
                    "cooldown_frames": "default for kind '%s'; read AnimalAIComponent attack_*_frames_between %s" % (a["kind"], WAK),
                    "range_tiles": "default for kind; read AnimalAIComponent attack_*_range " + WAK,
                },
            }
            if a["kind"] == "projectile" and not cnt:
                arow["_unverified"]["count"] = "wiki shows no count; read attack_ranged_entity_count " + WAK
            attacks.append(arow)
            row["attacks"].append(aid)
        if not row["attacks"] and r["attackType"].strip().lower() not in ("none",) and rule != "never" and r["faction"] != "helpless":
            row["_unverified"]["attacks"] = "wiki lists no attack; confirm in the entity XML"
        enemies.append(row)

    ai_rows = []
    for aid, (move, speed, accel, jump, grav, sight, keep, desc) in ARCHETYPES.items():
        used = [e["id"] for e in enemies if e["ai"] == aid]
        ai_rows.append({
            "id": aid, "description": desc, "move": move, "speed": speed, "accel": accel, "jump_speed": jump,
            "gravity": grav, "sight_tiles": sight, "keep_distance_tiles": keep, "used_by": used,
            "stage": min(e["stage"] for e in enemies if e["ai"] == aid) if used else "1b",
            "_unverified": {"speed": "tune in game against the Noita original", "accel": "tune in game",
                            "jump_speed": "tune in game"},
        })
    for b in BOSSES:
        e = next((x for x in enemies if x["id"] == b), None)
        ai_rows.append({
            "id": "boss_" + b, "description": "Boss fight for %s; designed in stage 1c." % (e["name_en"] if e else b),
            "move": None, "speed": None, "accel": None, "jump_speed": None, "gravity": None, "sight_tiles": None,
            "keep_distance_tiles": None, "used_by": [b], "stage": "1c", "_unverified": {},
        })

    biome_rows = []
    for loc, (zone, tier, note) in BIOME_MAP.items():
        biome_rows.append({"id": loc, "zone": zone, "tier": tier, "note": note or "-",
                           "used_by": sorted(e["id"] for e in enemies if loc in e["spawn_in"]), "_unverified": {}})
    zone_rows = [{"id": z, "condition": c, "hardmode": hm, "after_boss": ab, "terraria_check": None,
                  "_unverified": {}} for z, (c, hm, ab) in ZONES.items()]
    zone_rows[-1]["terraria_check"] = "false"
    balance_rows = [{"id": t, "meaning": m, "hp_mult": h, "dmg_mult": d, "defense": df, "spawn_weight": s,
                     "_unverified": {"hp_mult": "playtest", "dmg_mult": "playtest"}}
                    for t, (m, h, d, df, s) in BALANCE.items()]

    def write(name, desc, key, columns, rows_):
        with open(os.path.join(SHEETS, name + ".json"), "w", encoding="utf-8") as f:
            json.dump({"sheet": name, "description": desc, "key": key, "columns": columns, "rows": rows_},
                      f, ensure_ascii=False, indent=1)
            f.write("\n")

    os.makedirs(SHEETS, exist_ok=True)
    write("enemies", "Every Noita creature, one row each. Stage 1 = all of them in Terraria.", "id", {
        "id": {"type": "string", "desc": "Noita entity id (file name without .xml)."},
        "name_en": {"type": "string", "desc": "English name (fallback when Noita's translation file is missing)."},
        "name_fi": {"type": "string", "desc": "Original Finnish name."},
        "name_key": {"type": "string", "desc": "Key in Noita's data/translations/common.csv for the localized name."},
        "category": {"type": "string", "desc": "Wiki category."},
        "faction": {"type": "string", "desc": "Noita faction (herd). Same faction does not fight each other."},
        "noita_hp": {"type": "number", "desc": "Health as Noita displays it (internal hp x 25)."},
        "noita_entity": {"type": "string", "desc": "Entity XML inside data.wak."},
        "sprite": {"type": "string", "desc": "Sprite XML inside data.wak (from the entity's SpriteComponent image_file)."},
        "hitbox": {"type": "int[]", "desc": "[width, height] in Terraria pixels (Noita pixels x 2)."},
        "ai": {"type": "ref", "ref": "ai_archetypes", "desc": "Movement/behaviour archetype."},
        "attacks": {"type": "ref[]", "ref": "attacks", "desc": "Attacks this enemy uses."},
        "spawn_in": {"type": "ref[]", "ref": "biome_map", "desc": "Noita locations it spawns in (natural spawns only)."},
        "spawn_rule": {"type": "enum", "values": ["natural", "disguised", "boss_summon", "spawned_by_other", "holy_mountain_guard", "special_location", "never"], "desc": "How it enters a Terraria world."},
        "tier": {"type": "ref", "ref": "balance", "desc": "Progression tier: scales hp and damage."},
        "dmg_mult": {"type": "object", "desc": "Damage taken multiplier per Noita damage type."},
        "immunities": {"type": "string[]", "desc": "Noita status immunities."},
        "blood": {"type": "string", "desc": "Material it bleeds (used by stage 2 physics)."},
        "drops": {"type": "ref", "ref": "drops", "desc": "Loot table."},
        "stage": {"type": "enum", "values": ["1a", "1b", "1c"], "desc": "1a = first slice, 1b = all regular enemies, 1c = bosses, minions and special."},
    }, enemies)
    write("attacks", "Each attack of each enemy.", "id", {
        "id": {"type": "string", "desc": "<enemy>.<attack>"},
        "enemy": {"type": "ref", "ref": "enemies", "desc": "Owner."},
        "kind": {"type": "enum", "values": ["melee", "lunge", "projectile", "aura", "death_explosion", "summon", "retaliate", "heal", "support"], "desc": "How it is delivered."},
        "projectile": {"type": "ref", "ref": "projectiles", "allow": ["none"], "desc": "Projectile fired, or none."},
        "count": {"type": "int[]", "desc": "[min, max] projectiles per volley."},
        "damage": {"type": "object", "desc": "Noita damage per type (number or [min,max])."},
        "per_frames": {"type": "string", "desc": "'hit' = per hit, 'NF' = every N frames while touching/in the aura."},
        "cooldown_frames": {"type": "int", "desc": "Frames between uses (60 per second)."},
        "range_tiles": {"type": "number", "desc": "Max distance to start the attack, in Terraria tiles."},
        "wiki_text": {"type": "string", "desc": "Original wiki text, for reference."},
        "stage": {"type": "enum", "values": ["1a", "1b", "1c"], "desc": "Same as its enemy."},
    }, attacks)
    write("projectiles", "Projectiles enemies fire. Stage 3 reuses them as spells.", "id", {
        "id": {"type": "string", "desc": "Projectile id."},
        "name_en": {"type": "string", "desc": "Name."},
        "used_by": {"type": "ref[]", "ref": "enemies", "desc": "Enemies that fire it."},
        "noita_file": {"type": "string", "desc": "Projectile entity XML in data.wak (from the enemy's attack_ranged_entity_file)."},
        "sprite": {"type": "string", "desc": "Sprite (png or sprite xml) in data.wak."},
        "speed": {"type": "number", "desc": "Launch speed, Terraria pixels per frame."},
        "gravity": {"type": "number", "desc": "Gravity, pixels per frame^2 (0 = straight)."},
        "lifetime_frames": {"type": "int", "desc": "Frames before it expires."},
        "explosion_radius": {"type": "number", "desc": "Explosion radius in tiles (0 = none)."},
        "effect": {"type": "enum", "values": ["none", "acid", "fire", "ice", "electric", "poison", "polymorph", "teleport", "blind", "glue", "heal", "explosive", "light", "neutralize", "swap"], "desc": "Status or special effect on hit."},
        "terrain": {"type": "enum", "values": ["none", "dig", "melt", "burn", "explode"], "desc": "What it does to blocks (stage 2 physics)."},
        "stage": {"type": "enum", "values": ["1a", "1b", "1c"], "desc": "Earliest stage that needs it."},
    }, sorted(projectiles.values(), key=lambda p: p["id"]))
    write("ai_archetypes", "Behaviour archetypes; each generates one AI state machine.", "id", {
        "id": {"type": "string", "desc": "Archetype id."},
        "description": {"type": "string", "desc": "What it does."},
        "move": {"type": "enum", "values": ["ground", "ground_levitate", "hover", "phase", "fly", "hop", "climb", "burrow", "swim", "static"], "desc": "Movement mode."},
        "speed": {"type": "number", "desc": "Max speed, pixels/frame."},
        "accel": {"type": "number", "desc": "Acceleration, pixels/frame^2."},
        "jump_speed": {"type": "number", "desc": "Jump/levitation impulse."},
        "gravity": {"type": "bool", "desc": "Affected by gravity."},
        "sight_tiles": {"type": "number", "desc": "Notices the player within this many tiles."},
        "keep_distance_tiles": {"type": "number", "desc": "Preferred distance from the target (0 = close in)."},
        "used_by": {"type": "ref[]", "ref": "enemies", "desc": "Enemies using it."},
        "stage": {"type": "enum", "values": ["1a", "1b", "1c"], "desc": "Earliest stage that needs it."},
    }, ai_rows)
    write("biome_map", "Noita locations mapped to Terraria spawn zones.", "id", {
        "id": {"type": "string", "desc": "Noita location (wiki name)."},
        "zone": {"type": "ref", "ref": "terraria_zones", "desc": "Terraria zone."},
        "tier": {"type": "ref", "ref": "balance", "desc": "Tier of enemies coming from here."},
        "note": {"type": "string", "desc": "Why."},
        "used_by": {"type": "ref[]", "ref": "enemies", "desc": "Enemies spawning from it."},
    }, biome_rows)
    write("terraria_zones", "Terraria spawn zones and the exact check the mod makes.", "id", {
        "id": {"type": "string", "desc": "Zone id."},
        "condition": {"type": "string", "desc": "In plain words."},
        "hardmode": {"type": "bool", "desc": "Needs hardmode."},
        "after_boss": {"type": "enum", "values": ["none", "skeletron", "plantera"], "desc": "Boss that must be down."},
        "terraria_check": {"type": "string", "desc": "C# expression over Player p / spawn tile x,y, checked against the 1.4.5 decompile."},
    }, zone_rows)
    write("balance", "Progression tiers: how Noita numbers become Terraria numbers.", "id", {
        "id": {"type": "string", "desc": "Tier id."},
        "meaning": {"type": "string", "desc": "Where in Terraria's progression."},
        "hp_mult": {"type": "number", "desc": "Terraria life = noita_hp x this."},
        "dmg_mult": {"type": "number", "desc": "Terraria damage = Noita damage x this."},
        "defense": {"type": "int", "desc": "Terraria defense."},
        "spawn_weight": {"type": "number", "desc": "Relative spawn weight vs vanilla enemies."},
    }, balance_rows)
    write("drops", "Loot tables.", "id", {
        "id": {"type": "string", "desc": "Table id."},
        "description": {"type": "string", "desc": "What drops."},
        "rule": {"type": "string", "desc": "Exact rule."},
        "stage": {"type": "enum", "values": ["1a", "1b", "1c"], "desc": "Earliest stage."},
    }, [
        {"id": "none", "description": "Nothing.", "rule": "no drop", "stage": "1a", "_unverified": {}},
        {"id": "noita_gold", "description": "Gold like Noita: more health, more money.",
         "rule": "copper coins = round(terraria_life * 0.6); 1 in 12 a Heart, 1 in 15 a Mana Star", "stage": "1a",
         "_unverified": {"rule": "playtest"}},
        {"id": "boss_reward", "description": "Boss loot.", "rule": None, "stage": "1c", "_unverified": {}},
    ])
    print("enemies %d, attacks %d, projectiles %d, archetypes %d, biomes %d" % (
        len(enemies), len(attacks), len(projectiles), len(ai_rows), len(biome_rows)))
    missing = [e["id"] for e in enemies if e["ai"] is None]
    if missing:
        print("no archetype:", missing)


if __name__ == "__main__":
    main()
