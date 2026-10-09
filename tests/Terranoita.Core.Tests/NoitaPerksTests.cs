using System.Collections.Generic;
using System.Linq;
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

        sealed class Host : ShotHostBase { }

        [Fact]
        public void APerksFuncRunsOnThePlayerEntity()
        {
            // as Noita's EXTRA_HP and a perk adding a game effect: the func reads and writes the picker's components
            const string list = @"perk_list = {
	{ id = ""MORE_HP"", func = function( item, who, name )
		local dm = EntityGetFirstComponent( who, ""DamageModelComponent"" )
		ComponentSetValue2( dm, ""max_hp"", ComponentGetValue2( dm, ""max_hp"" ) + 1 )
	end },
	{ id = ""FIRE_PROOF"", func = function( item, who, name )
		EntityAddComponent( who, ""GameEffectComponent"", { effect = ""PROTECTION_FIRE"", frames = ""-1"" } )
	end },
	{ id = ""NO_FUNC"" },
}";
            var files = new Dictionary<string, string> { [NoitaPerks.ListFile] = list };
            var store = new LuaShotScripts(new Host(), p => files.TryGetValue(p, out var t) ? t : null);
            int player = store.CreateEntity("DEBUG_NAME:player", "player_unit", 0, 0, new (string, IDictionary<string, string>)[]
            {
                ("DamageModelComponent", new Dictionary<string, string> { ["hp"] = "4", ["max_hp"] = "4" }),
            });
            Assert.True(store.RunPerk("MORE_HP", player, 0, 1));
            Assert.Equal("5", store.Components(player, "DamageModelComponent").Single().Get("max_hp"));
            Assert.True(store.RunPerk("FIRE_PROOF", player, 0, 1));
            Assert.Equal("PROTECTION_FIRE", store.Components(player, "GameEffectComponent").Single().Get("effect"));
            Assert.False(store.RunPerk("NO_FUNC", player, 0, 1));
            Assert.Throws<System.ArgumentException>(() => store.RunPerk("NOT_A_PERK", player, 0, 1));
        }

        [Fact]
        public void AMissingListIsAnError()
        {
            Assert.Throws<System.InvalidOperationException>(() => NoitaPerks.Read(p => null));
        }
    }
}
