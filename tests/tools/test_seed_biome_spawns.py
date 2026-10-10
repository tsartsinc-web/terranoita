"""seed_biome_spawns: one row per biome with a zone and placement function; the author's numbers survive a rerun."""
import os
import sys
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import seed_biome_spawns as seed  # noqa: E402

BIOMES = [{"id": "Mines", "zone": "underground_dirt"}, {"id": "Holy Mountain", "zone": "none"}, {"id": "Nowhere", "zone": "cavern"}]


class SeedBiomeSpawnsTests(unittest.TestCase):
    def test_rows(self):
        rows = seed.rows(BIOMES, {})
        self.assertEqual(len(rows), 2 * len(seed.FUNCTIONS))   # Holy Mountain has no zone
        mines = [r for r in rows if r["biome"] == "Mines"]
        self.assertTrue(all(r["script"] == "coalmine" and r["stage"] == "4" for r in mines))
        self.assertEqual({r["function"] for r in mines}, set(seed.FUNCTIONS))
        self.assertEqual(next(r for r in rows if r["biome"] == "Nowhere")["script"], "none")

    def test_author_numbers_kept(self):
        old = {"Mines:spawn_wands": {"per_10k_tiles": 3.5, "_unverified": {}}}
        r = next(r for r in seed.rows(BIOMES, old) if r["id"] == "Mines:spawn_wands")
        self.assertEqual((r["per_10k_tiles"], r["_unverified"]), (3.5, {}))


if __name__ == "__main__":
    unittest.main()
