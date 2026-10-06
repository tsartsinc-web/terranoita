"""apply_spells on made-up facts in the tncli spells format.

  python -m unittest discover -s tests/tools
"""
import os
import sys
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import apply_spells  # noqa: E402
import preflight  # noqa: E402


def spell(**kw):
    s = {"name_key": "$action_x", "name_en": "X", "type": "projectile", "sprite": "x.png", "mana": 5.0, "max_uses": None,
         "price": 100.0, "spawn_level": [0, 1], "spawn_probability": [1.0, 0.5], "projectiles": ["x.xml"], "triggers": [],
         "draws": 0, "config_add": {"fire_rate_wait": 3.0}, "config_mul": {}, "config_set": {}, "reload_add": 0.0,
         "conditional": False, "calls": [], "unparsed": [], "fields": {}}
    s.update(kw)
    return s


class ApplySpellsTests(unittest.TestCase):
    def test_plain_spell_is_data(self):
        r = apply_spells.spell_row("X", spell())
        self.assertEqual((r["port"], r["max_uses"], r["trigger_kind"], r["extra_entities"]), ("data", -1, "none", "none"))
        self.assertEqual(r["_unverified"], {})

    def test_trigger_and_extra_entities(self):
        r = apply_spells.spell_row("T", spell(triggers=[{"kind": "timer", "file": "t.xml", "draws": 1, "frames": 20}],
                                              config_set={"extra_entities+": "a.xml,"}))
        self.assertEqual((r["trigger_kind"], r["trigger_file"], r["trigger_draws"], r["trigger_frames"]), ("timer", "t.xml", 1, 20))
        self.assertEqual(r["extra_entities"], "a.xml,")
        self.assertEqual(r["port"], "data")

    def test_odd_function_needs_hand_work(self):
        r = apply_spells.spell_row("O", spell(conditional=True, calls=["SetRandomSeed"], unparsed=["if a then"]))
        self.assertEqual(r["port"], "hand")
        self.assertIn("SetRandomSeed", r["_unverified"]["port"])

    def test_rows_pass_the_sheet_checks(self):
        rows = [apply_spells.spell_row("X", spell()), apply_spells.spell_row("O", spell(calls=["Foo"]))]
        sheets = {"spells": {"sheet": "spells", "key": "id", "columns": apply_spells.SPELL_COLUMNS, "rows": rows}}
        kinds = {(p[1], p[3]) for p in preflight.check(sheets)}
        self.assertEqual(kinds, {("O", "UNVERIFIED")})

    def test_wand_from_entity(self):
        w = {"ability": {"component": "AbilityComponent", "attrs": {"ui_name": "W", "mana_max": "100", "mana_charge_speed": "30"},
                         "children": [{"component": "gun_config", "attrs": {"actions_per_round": "2", "reload_time": "20",
                                                                            "deck_capacity": "5", "shuffle_deck_when_empty": "1"}},
                                      {"component": "gunaction_config", "attrs": {"fire_rate_wait": "9"}}]},
             "scripts": ["data/scripts/gun/procedural/x.lua"]}
        r = apply_spells.wand_row("data/entities/items/wand_x.xml", w)
        self.assertEqual((r["id"], r["shuffle"], r["spells_per_cast"], r["capacity"], r["cast_delay"]), ("wand_x", True, 2, 5, 9.0))
        self.assertIsNone(r["sprite"])
        self.assertIn("x.lua", r["_unverified"]["sprite"])


if __name__ == "__main__":
    unittest.main()
