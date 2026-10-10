"""seed_status_effects: the ELECTROCUTION row from electricity_facts.json (a GameEffect, not in status_list.lua)."""
import json
import os
import shutil
import sys
import tempfile
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import seed_status_effects as seed  # noqa: E402

FACTS = {"electrocution_effect": {"file": "data/entities/misc/effect_electricity.xml",
                                  "GameEffectComponent": {"effect": "ELECTROCUTION", "frames": "40", "disable_movement": "1"}}}


class ElectrocutionRowTests(unittest.TestCase):
    def test_row_from_the_game_effect(self):
        r = seed.electrocution_row(FACTS)
        self.assertEqual((r["id"], r["seconds"], r["harmful"], r["stage"]), ("ELECTROCUTION", 0.667, True, "3"))
        self.assertIn("disable_movement", r["mechanic"])
        self.assertIn("Electrified", r["mechanic"])

    def test_upsert_keeps_other_rows_and_replaces_its_own(self):
        d = tempfile.mkdtemp()
        try:
            sheet = os.path.join(d, "status_effects.json")
            shutil.copy(os.path.join(ROOT, "design", "sheets", "status_effects.json"), sheet)
            facts = os.path.join(d, "facts.json")
            with open(facts, "w", encoding="utf-8") as f:
                json.dump(FACTS, f)
            before = len([r for r in json.load(open(sheet, encoding="utf-8"))["rows"] if r["id"] != "ELECTROCUTION"])
            seed.upsert_electrocution(facts, sheet)
            seed.upsert_electrocution(facts, sheet)
            rows = json.load(open(sheet, encoding="utf-8"))["rows"]
            self.assertEqual(len(rows), before + 1)
            self.assertEqual(sum(r["id"] == "ELECTROCUTION" for r in rows), 1)
        finally:
            shutil.rmtree(d)


if __name__ == "__main__":
    unittest.main()
