"""extract_liquids: CellDataChild inheritance, reacts_as (Noita _inherit_reactions) and which Reaction rules are ours."""
import os
import shutil
import sys
import tempfile
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import extract_liquids as ex  # noqa: E402

XML = """<?xml version="1.0" encoding="UTF-8" ?>
<Materials>
  <CellData name="water" cell_type="liquid" tags="[liquid],[water],[evaporable]" density="3" />
  <CellDataChild name="water_salt" _parent="water" _inherit_reactions="1" density="3.5" />
  <CellDataChild name="water_salt_deep" _parent="water_salt" _inherit_reactions="1" />
  <CellDataChild name="water_fading" _parent="water" />
  <CellData name="lava" cell_type="liquid" tags="[liquid],[hot]" />
  <CellData name="rock_static" cell_type="solid" tags="[solid]" />
  <CellData name="steam" cell_type="gas" tags="[gas]" />
  <Reaction probability="80" input_cell1="water" input_cell2="lava" output_cell1="steam" output_cell2="rock_static" />
  <Reaction probability="10" input_cell1="[water]" input_cell2="[hot]" output_cell1="steam" output_cell2="air" />
  <Reaction probability="5" input_cell1="[evaporable]_vapour" input_cell2="air" output_cell1="air" output_cell2="air" />
  <Reaction probability="5" input_cell1="rock_static" input_cell2="[solid]" output_cell1="air" output_cell2="air" />
  <Reaction probability="7" input_cell1="water_salt" input_cell2="rock_static" output_cell1="water" output_cell2="rock_static"
            input_cell3="lava" output_cell3="steam" direction="top" blob_radius1="2" blob_restrict_to_input_material1="1" />
  <Reaction fast_reaction="1" probability="100" input_cell1="[unknown_tag]" input_cell2="rock_static" output_cell1="air" output_cell2="air" />
</Materials>
"""


def row(mats, n):
    tags = [t.strip("[]") for t in (mats[n].get("tags") or "").split(",") if t.strip()]
    return {"id": n, "tags": tags, "reacts_as": ex.reacts_as(mats, n)}


class ExtractLiquidsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp()
        path = os.path.join(cls.tmp, "materials.xml")
        with open(path, "w", encoding="utf-8") as f:
            f.write(XML)
        cls.mats, cls.root = ex.load(path)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp)

    def test_child_takes_parent_attributes_unless_set(self):
        self.assertEqual(self.mats["water_salt"]["cell_type"], "liquid")
        self.assertEqual(self.mats["water_salt"]["density"], "3.5")
        self.assertEqual(self.mats["water_salt_deep"]["density"], "3.5")   # through two links
        self.assertIn("[water]", self.mats["water_salt_deep"]["tags"])

    def test_reacts_as_follows_inherit_reactions_only(self):
        self.assertEqual(ex.reacts_as(self.mats, "water_salt"), ["water"])
        self.assertEqual(ex.reacts_as(self.mats, "water_salt_deep"), ["water_salt", "water"])
        self.assertEqual(ex.reacts_as(self.mats, "water_fading"), [])   # no _inherit_reactions
        self.assertEqual(ex.reacts_as(self.mats, "water"), [])

    def test_rules_selected_by_name_parent_and_tag(self):
        rules = ex.select_reactions(self.root, [row(self.mats, "water_salt")])
        inputs = [(r["input1"], r["input2"]) for r in rules]
        self.assertIn(("water", "lava"), inputs)               # by a material it reacts as
        self.assertIn(("[water]", "[hot]"), inputs)            # by a tag it carries
        self.assertIn(("[evaporable]_vapour", "air"), inputs)  # by the tag part of a [tag]_suffix cell
        self.assertNotIn(("rock_static", "[solid]"), inputs)   # nothing of ours
        self.assertNotIn(("[unknown_tag]", "rock_static"), inputs)

    def test_rule_columns(self):
        rules = ex.select_reactions(self.root, [row(self.mats, "water_salt")])
        r = next(r for r in rules if r["input1"] == "water_salt")
        self.assertEqual((r["input3"], r["output3"], r["direction"]), ("lava", "steam", "top"))
        self.assertEqual((r["blob_radius1"], r["blob_restrict1"], r["blob_restrict2"]), (2.0, True, False))
        self.assertEqual(r["probability"], 7.0)
        plain = next(r for r in rules if r["input1"] == "water")
        self.assertEqual((plain["input3"], plain["output3"], plain["direction"], plain["fast"]), ("none", "none", "none", False))
        self.assertEqual([r["id"] for r in rules], ["r%03d" % i for i in range(len(rules))])

    def test_fast_reactions(self):
        rules = ex.select_reactions(self.root, [row(self.mats, "steam"), {"id": "x", "tags": ["unknown_tag"], "reacts_as": []}])
        fast = next(r for r in rules if r["input1"] == "[unknown_tag]")
        self.assertTrue(fast["fast"])


if __name__ == "__main__":
    unittest.main()
