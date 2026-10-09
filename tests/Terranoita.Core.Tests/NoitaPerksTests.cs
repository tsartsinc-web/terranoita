using System.Collections.Generic;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    public class NoitaPerksTests
    {
        // shaped like Noita's perk_list.lua: helpers loaded with dofile_once, engine calls only inside func
        const string List = @"dofile_once(""data/scripts/perks/perk_utilities.lua"")
STACKABLE_YES = true
STACKABLE_NO = false
perk_list =
{
	{
		id = ""CRITICAL_HIT"",
		ui_name = ""$perk_critical_hit"",
		perk_icon = ""data/items_gfx/perks/critical_hit.png"",
		stackable = STACKABLE_YES,
		stackable_maximum = 4,
		game_effect = ""CRITICAL_HIT_BOOST"",
		usable_by_enemies = true,
	},
	--[[ { id = ""OLD_PERK"" }, ]]--
	{
		id = ""MAP"",
		ui_name = ""$perk_map"",
		not_in_default_perk_pool = true,
		func = function( entity_perk_item, entity_who_picked, item_name )
			EntityLoadChild( entity_who_picked, ""data/entities/misc/perks/map.xml"" )
		end,
	},
}";

        [Fact]
        public void ReadsThePerkTableByRunningTheList()
        {
            var files = new Dictionary<string, string> { [NoitaPerks.ListFile] = List, ["data/scripts/perks/perk_utilities.lua"] = "function perk_spawn() end" };
            var perks = NoitaPerks.Read(p => files.TryGetValue(p, out var t) ? t : null);
            Assert.Equal(new[] { "CRITICAL_HIT", "MAP" }, perks.ConvertAll(p => p.Id));
            var crit = perks[0];
            Assert.True(crit.Stackable && crit.UsableByEnemies && !crit.HasFunc);
            Assert.Equal(4, crit.StackableMaximum);
            Assert.Equal("CRITICAL_HIT_BOOST", Assert.Single(crit.GameEffects));
            Assert.Equal("data/items_gfx/perks/critical_hit.png", crit.PerkIcon);
            Assert.True(perks[1].NotInDefaultPool && perks[1].HasFunc && !perks[1].Stackable);
        }

        [Fact]
        public void AMissingListIsAnError()
        {
            Assert.Throws<System.InvalidOperationException>(() => NoitaPerks.Read(p => null));
        }
    }
}
