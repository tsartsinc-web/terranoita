using System.Collections.Generic;
using System.Linq;
using Terranoita.Noita;
using Terranoita.Spells;
using Xunit;

namespace Terranoita.Tests
{
    public class SpellTests
    {
        // A made-up table in gun_actions.lua's layout (not Noita's data): the reader is checked on the shapes it meets.
        const string Lua = @"
dofile_once(""data/scripts/lib/utilities.lua"")
-- comment with { braces }
actions =
{
	{
		id          = ""TEST_BOLT"",
		name 		= ""$action_test_bolt"",
		description = ""$actiondesc_test_bolt"",
		sprite 		= ""data/ui_gfx/gun_actions/test_bolt.png"",
		related_projectiles	= {""data/entities/projectiles/deck/test_bolt.xml""},
		type 		= ACTION_TYPE_PROJECTILE,
		spawn_level                       = ""0,1,2"", -- TEST_BOLT
		spawn_probability                 = ""1,0.5,0.25"",
		price = 100,
		mana = 5,
		--max_uses = 50,
		action 		= function()
			add_projectile(""data/entities/projectiles/deck/test_bolt.xml"")
			c.fire_rate_wait = c.fire_rate_wait + 3
			c.spread_degrees = c.spread_degrees - 1.5
		end,
	},
	{
		id          = ""TEST_TRIGGER"",
		type 		= ACTION_TYPE_PROJECTILE,
		mana = 10,
		max_uses    = 3,
		action 		= function()
			add_projectile_trigger_timer(""data/entities/projectiles/deck/test_timer.xml"", 20, 1)
			c.fire_rate_wait = c.fire_rate_wait + 10
			current_reload_time = current_reload_time + 5
		end,
	},
	{
		id          = ""TEST_ODD"",
		type 		= ACTION_TYPE_OTHER,
		mana = 0,
		action 		= function()
			if reflecting then return end
			for i=1,2 do
				draw_actions( 1, true )
			end
			c.extra_entities = c.extra_entities .. ""data/entities/misc/test.xml,""
			c.damage_projectile_add = c.damage_projectile_add * 2
			draw_actions( 1, true )
			SetRandomSeed( 1, 2 )
		end,
	},
}
";

        [Fact]
        public void ReadsGunActionsTable()
        {
            var a = GunActions.Parse(Lua);
            Assert.Equal(new[] { "TEST_BOLT", "TEST_TRIGGER", "TEST_ODD" }, a.Select(x => x.Id));

            var bolt = a[0];
            Assert.Equal("$action_test_bolt", bolt.Name);
            Assert.Equal("projectile", bolt.Type);
            Assert.Equal(new[] { 0, 1, 2 }, bolt.SpawnLevel);
            Assert.Equal(new[] { 1f, 0.5f, 0.25f }, bolt.SpawnProbability);
            Assert.Equal(5f, bolt.Mana);
            Assert.Null(bolt.MaxUses);                       // commented out
            Assert.Equal(new[] { "data/entities/projectiles/deck/test_bolt.xml" }, bolt.RelatedProjectiles);
            Assert.Equal(new[] { "data/entities/projectiles/deck/test_bolt.xml" }, bolt.Projectiles);
            Assert.Equal(3f, bolt.ConfigAdd["fire_rate_wait"]);
            Assert.Equal(-1.5f, bolt.ConfigAdd["spread_degrees"]);
            Assert.False(bolt.Conditional);
            Assert.Empty(bolt.Unparsed);

            var trig = a[1];
            Assert.Equal(3, trig.MaxUses);
            var t = Assert.Single(trig.Triggers);
            Assert.Equal(("timer", 20, 1), (t.Kind, t.Frames.Value, t.Draws));
            Assert.Equal(5f, trig.ReloadAdd);

            var odd = a[2];
            Assert.True(odd.Conditional);
            Assert.Equal(1, odd.Draws);                      // only the one outside the loop
            Assert.Equal(2f, odd.ConfigMul["damage_projectile_add"]);
            Assert.Equal("data/entities/misc/test.xml,", odd.ConfigSet["extra_entities+"]);
            Assert.Contains("SetRandomSeed", odd.Calls);
            Assert.Contains("draw_actions", odd.Calls);
            Assert.Equal(5, odd.Unparsed.Count);             // if-line, for, inner draw, end, the seed call
        }

        static Spell Bolt(string id = "BOLT", float mana = 5) =>
            new Spell { Id = id, Type = "projectile", Mana = mana, Projectiles = new[] { id.ToLower() + ".xml" },
                        ConfigAdd = new Dictionary<string, float> { ["fire_rate_wait"] = 2 } };
        static Spell Double() => new Spell { Id = "DOUBLE", Type = "draw_many", Mana = 0, Draws = 2 };
        static Spell Damage() => new Spell { Id = "DAMAGE", Type = "modifier", Mana = 5, Draws = 1,
                                             ConfigAdd = new Dictionary<string, float> { ["damage_projectile_add"] = 0.4f, ["fire_rate_wait"] = 5 } };

        static Wand MakeWand(params Spell[] slots) => new Wand
        {
            SpellsPerCast = 1, CastDelay = 10, RechargeTime = 30, ManaMax = 100, ManaChargePerSecond = 60,
            Capacity = slots.Length, Slots = slots.ToList(),
        };

        [Fact]
        public void CastsInOrderAndRechargesAtTheEnd()
        {
            var g = new Gun(MakeWand(Bolt("A"), Bolt("B")));
            var r1 = g.Cast();
            Assert.Equal(new[] { "A" }, r1.Played);
            Assert.Equal(12, r1.CastDelay);                  // wand 10 + spell 2
            Assert.Equal(0, r1.Recharge);
            Assert.Null(g.Cast());                           // cooling down
            g.Tick(12);
            var r2 = g.Cast();
            Assert.Equal(new[] { "B" }, r2.Played);
            Assert.Equal(30, r2.Recharge);                   // deck empty -> recharge
            Assert.Equal(30, r2.Wait);
            g.Tick(30);
            Assert.Equal(new[] { "A" }, g.Cast().Played);   // starts over
        }

        [Fact]
        public void ModifiersAndMulticastShareOneShot()
        {
            var g = new Gun(MakeWand(Damage(), Double(), Bolt("A"), Bolt("B")));
            var r = g.Cast();
            Assert.Equal(new[] { "DAMAGE", "DOUBLE", "A", "B" }, r.Played);
            Assert.Equal(new[] { "a.xml", "b.xml" }, r.Shot.Projectiles.Select(p => p.File));
            Assert.Equal(0.4f, r.Shot.Get("damage_projectile_add"));
            Assert.Equal(10 + 5 + 2 + 2, r.CastDelay);
            Assert.Equal(30, r.Recharge);
            Assert.Equal(100 - 15, g.Mana);
        }

        [Fact]
        public void WrapsWhenTheDeckRunsOutMidCast()
        {
            var g = new Gun(MakeWand(Bolt("A"), Double(), Bolt("B")));
            g.Cast(); g.Tick(100);                           // A
            var r = g.Cast();                                // DOUBLE draws B, then wraps to A
            Assert.Equal(new[] { "DOUBLE", "B", "A" }, r.Played);
            Assert.True(r.Wrapped);
            Assert.Equal(30, r.Recharge);
            g.Tick(100);
            Assert.Equal(new[] { "A" }, g.Cast().Played);
        }

        [Fact]
        public void SkipsSpellsWithoutManaOrUses()
        {
            var limited = Bolt("L");
            limited.MaxUses = 1;
            var g = new Gun(MakeWand(Bolt("BIG", mana: 500), limited, Bolt("A")));
            var r = g.Cast();
            Assert.Equal(new[] { "L" }, r.Played);           // BIG skipped (no mana)
            Assert.Equal(0, g.UsesLeft(1));
            g.Tick(1000);
            r = g.Cast();                                    // A, then the deck is empty -> recharge
            Assert.Equal(new[] { "A" }, r.Played);
            g.Tick(1000);
            r = g.Cast();                                    // BIG skipped, L has no uses, A
            Assert.Equal(new[] { "A" }, r.Played);
        }

        [Fact]
        public void TriggerCarriesItsPayloadAndAlwaysCastIsFree()
        {
            var trig = new Spell { Id = "TRIG", Mana = 10, Projectiles = new string[0],
                                   Triggers = new[] { new Trigger { Kind = "hit_world", File = "t.xml", Draws = 1 } } };
            var wand = MakeWand(trig, Bolt("A"), Bolt("B"));
            wand.AlwaysCast.Add(Bolt("AC", mana: 50));
            var g = new Gun(wand);
            var r = g.Cast();
            Assert.Equal(new[] { "AC", "TRIG", "A" }, r.Played);
            Assert.Equal(100 - 10 - 5, g.Mana);              // always-cast costs nothing
            var p = r.Shot.Projectiles.Single(x => x.Trigger != null);
            Assert.Equal("a.xml", Assert.Single(p.Payload.Projectiles).File);
            Assert.Equal(10 + 2 + 2, r.CastDelay);           // always-cast + payload cast delay count for the wand
        }

        [Fact]
        public void ShuffleIsRepeatableWithASeed()
        {
            Spell[] slots = Enumerable.Range(0, 8).Select(i => Bolt("S" + i)).ToArray();
            var w1 = MakeWand(slots); w1.Shuffle = true;
            var w2 = MakeWand(slots); w2.Shuffle = true;
            var a = new Gun(w1, new System.Random(7)).Deck.ToList();
            var b = new Gun(w2, new System.Random(7)).Deck.ToList();
            Assert.Equal(a, b);
            Assert.Equal(slots.Select(s => s.Id).OrderBy(x => x), a.OrderBy(x => x));
        }

        [Fact]
        public void ManaCharges()
        {
            var g = new Gun(MakeWand(Bolt("A", mana: 60)));
            g.Cast();
            Assert.Equal(40, g.Mana);
            g.Tick(30);                                      // 60 per second
            Assert.Equal(70, g.Mana);
            g.Tick(600);
            Assert.Equal(100, g.Mana);
        }
    }
}

namespace Terranoita.Tests
{
    public class SpellSheetTests
    {
        [Xunit.Fact]
        public void SheetRowsBecomeAWorkingWand()
        {
            var bolt = new Terranoita.Generated.SpellDef { Id = "B", Type = "projectile", Mana = 5, MaxUses = -1, Projectiles = new[] { "b.xml" },
                TriggerKind = "none", ConfigAdd = new System.Collections.Generic.Dictionary<string, float> { ["fire_rate_wait"] = 4 } };
            var trig = new Terranoita.Generated.SpellDef { Id = "T", Type = "projectile", Mana = 10, MaxUses = 2, Projectiles = new string[0],
                TriggerKind = "death", TriggerFile = "t.xml", TriggerDraws = 1 };
            var w = Terranoita.Spells.FromSheets.Wand(new Terranoita.Generated.WandDef { SpellsPerCast = 1, CastDelay = 8, RechargeTime = 20, ManaMax = 50, ManaChargeSpeed = 10, Capacity = 2, SpeedMultiplier = 1 },
                new[] { trig, bolt });
            var r = new Terranoita.Spells.Gun(w).Cast();
            Xunit.Assert.Equal(new[] { "T", "B" }, r.Played);
            Xunit.Assert.Equal(12, r.CastDelay);
            Xunit.Assert.Equal(20, r.Recharge);
        }
    }
}
