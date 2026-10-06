"""apply_facts on facts in the newer tncli format (components, found_by), against a copy of the real sheets.

  python -m unittest discover -s tests/tools
"""
import copy
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import apply_facts  # noqa: E402

FACTS = os.path.join(ROOT, "design", "sources", "noita_facts.json")


def read_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def comp(name, children=None, **attrs):
    c = {"component": name, "attrs": {k: str(v) for k, v in attrs.items()}}
    if children:
        c["children"] = children
    return c


class ApplyFactsTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp()
        shutil.copytree(os.path.join(ROOT, "design", "sheets"), os.path.join(cls.tmp, "sheets"))
        facts = read_json(FACTS)
        f = {k: v for k, v in facts.items() if k in ("hpcrystal", "wizard_hearty", "weakspirit", "worm")}
        f["hpcrystal"]["components"] = [comp("DamageModelComponent", hp=20), comp("LuaComponent", script_source_file="x.lua")]
        f["weakspirit"]["components"] = [comp("DamageModelComponent", hp=3), comp("CharacterPlatformingComponent")]
        f["worm"]["components"] = [comp("WormComponent", speed=7), comp("WormAIComponent", speed=5)]
        hearty = f["wizard_hearty"]["ranged"][0]["projectile"]
        hearty["components"] = [comp("ProjectileComponent", damage=0.3), comp("GameEffectComponent", effect="BLINDNESS")]
        f["nest_fly"] = copy.deepcopy(facts["hpcrystal"])
        f["nest_fly"].update(entity="data/entities/buildings/flynest.xml", found_by="file_words", candidates=[],
                             components=[comp("DamageModelComponent", hp=4)], ranged=[])
        path = os.path.join(cls.tmp, "facts.json")
        with open(path, "w", encoding="utf-8") as out:
            json.dump(f, out)
        cls.out = subprocess.run([sys.executable, "-I", os.path.join(ROOT, "tools", "apply_facts.py"), path, "--stage", "1b",
                                  "--sheets", os.path.join(cls.tmp, "sheets"),
                                  "--sounds", os.path.join(ROOT, "design", "sources", "noita_sounds.txt")],
                                 capture_output=True, text=True, check=True).stdout
        cls.rows = {}
        for s in ("enemies", "attacks", "projectiles"):
            for r in read_json(os.path.join(cls.tmp, "sheets", s + ".json"))["rows"]:
                cls.rows[r["id"]] = r

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp)

    def test_creature_without_any_movement_component_is_static(self):
        e = self.rows["hpcrystal"]
        self.assertEqual((e["walks"], e["flies"], e["run_speed"], e["gravity"]), (False, False, 0.0, 0.0))
        self.assertNotIn("walks", e["_unverified"])
        self.assertGreater(e["sight_tiles"], 0)

    def test_worm_is_left_for_its_components_with_a_note(self):
        self.assertIsNone(self.rows["worm"]["run_speed"])
        self.assertIn("worm: moves without CharacterPlatformingComponent (WormAIComponent, WormComponent)", self.out)

    def test_projectile_effect_from_game_effect(self):
        p = self.rows["wizard_hearty_shot"]
        self.assertEqual(p["effect"], "blind")
        self.assertNotIn("effect", p["_unverified"])
        self.assertIn("BLINDNESS", p["_sources"]["effect"])

    def test_creature_with_no_attack_components_has_its_empty_attacks_verified(self):
        self.assertNotIn("attacks", self.rows["weakspirit"]["_unverified"])

    def test_found_entity_replaces_a_wiki_seeded_id(self):
        e = self.rows["nest_fly"]
        self.assertEqual(e["noita_entity"], "data/entities/buildings/flynest.xml")
        self.assertNotIn("id", e["_unverified"])
        self.assertIn("nest_fly: entity found by file_words", self.out)


class EffectRulesTest(unittest.TestCase):
    def effect(self, *comps):
        return apply_facts.derive_effect({"components": list(comps)}, "p.xml")[0]

    def test_rules(self):
        self.assertEqual(self.effect(comp("ProjectileComponent", children=[comp("config_explosion", create_cell_material="acid", explosion_radius=4)])), "acid")
        self.assertEqual(self.effect(comp("ProjectileComponent", children=[comp("damage_by_type", fire=0.2)])), "fire")
        self.assertEqual(self.effect(comp("ProjectileComponent", on_death_explode=1, children=[comp("config_explosion", explosion_radius=20)])), "explosive")
        self.assertEqual(self.effect(comp("ProjectileComponent", damage=0.3)), "none")
        self.assertEqual(self.effect(comp("ProjectileComponent", damage=-0.2)), "heal")
        self.assertEqual(self.effect(comp("ParticleEmitterComponent", emitted_material_name="radioactive_liquid_fading")), "poison")
        self.assertIsNone(self.effect(comp("ProjectileComponent"), comp("HomingComponent")))
        self.assertIsNone(apply_facts.derive_effect({}, "p.xml")[0])


if __name__ == "__main__":
    unittest.main()
