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


class ShotRowPhysicsTests(unittest.TestCase):
    """Fields a file leaves unset take Noita's documented defaults; a projectile lightning keeps its own blast."""

    BOLT = {"components": [
        {"component": "VelocityComponent", "attrs": {"apply_terminal_velocity": "0"}},
        {"component": "ProjectileComponent", "attrs": {"on_collision_die": "0", "die_on_liquid_collision": "1"}},
        {"component": "LightningComponent", "attrs": {"is_projectile": "1"},
         "children": [{"component": "config_explosion", "attrs": {"explosion_radius": "35"}}]}]}

    def test_documented_defaults(self):
        r = apply_spells.shot_row("bolt.xml", self.BOLT)
        self.assertEqual((r["air_friction"], r["liquid_drag"], r["bounce_energy"], r["low_velocity_limit"]), (0.55, 1.0, 0.5, 50.0))
        self.assertEqual(r["terminal_velocity"], -1.0)   # apply_terminal_velocity 0
        self.assertEqual((r["die_on_hit"], r["die_on_liquid"], r["die_on_low_velocity"], r["penetrate_world"]),
                         (False, True, False, False))

    def test_lightning_blast(self):
        r = apply_spells.shot_row("bolt.xml", self.BOLT)
        self.assertEqual((r["lightning_radius"], r["lightning_damage"]), (35.0, apply_spells.LIGHTNING_DAMAGE_DEFAULT))
        self.assertIn("lightning_damage", r["_unverified"])
        plain = apply_spells.shot_row("p.xml", {"components": [{"component": "ProjectileComponent", "attrs": {"damage": "1"}}]})
        self.assertEqual((plain["lightning_radius"], plain["lightning_damage"], plain["terminal_velocity"]), (0.0, 0.0, 1000.0))
        self.assertEqual(plain["_unverified"], {})
