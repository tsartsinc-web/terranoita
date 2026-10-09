# Noita perks in Terranoita (PC-34)

Source: the author's Noita, `data/scripts/perks/perk_list.lua` (106 perks once the commented-out ones are removed);
names and descriptions from `data/translations/common.csv` (read 2026-10-10 with `tncli wak-get`; the table is made by a
script from those files, nothing typed by hand). Author's design (final, 2026-10-09): perks are ITEMS dropped by
Terraria bosses (more on Expert/Master/Legendary), used from the hand, bound to the character and saved with it; on
death all perks are lost, and with more than 10 a quarter of them (random) drop as perk items at the death spot.

## How a perk runs (stage-3 rule: Noita's own code)
- The perk item stores the perk id. Using it runs perk_list.lua's `func(entity_perk_item, entity_who_picked, item_name,
  pickup_count)` in our Lua (MoonSharp, LuaCulture) with the player as an entity of the script store.
- The player entity in the store (new, Core): the Noita components perk funcs read and write, mapped to Terraria:
  DamageModelComponent (hp / max_hp in Noita units, 1 = 25 life), CharacterDataComponent and
  CharacterPlatformingComponent (flight time, speed, gravity), WalletComponent (coins), GameEffectComponent (below),
  ShotEffectComponent (below), LuaComponent (runs every frame like shot scripts; script_damage_received...), child
  entities (EntityLoadChild: fields, shields, ghosts).
- game_effect -> Terraria: PROTECTION_FIRE = fire debuffs and lava burn immune; PROTECTION_EXPLOSION = no explosion
  damage; PROTECTION_MELEE = no contact damage; PROTECTION_ELECTRICITY = Electrified immune; PROTECTION_RADIOACTIVITY =
  Poisoned / Venom immune; PROTECTION_FREEZE = Frozen / Chilled immune; BREATH_UNDERWATER = Gills;
  MOVEMENT_FASTER = run speed by Noita's value; CRITICAL_HIT_BOOST / CRITS_2X_DAMAGE = crits of our casts;
  INVISIBILITY = Noita creatures lose the player; KNOCKBACK_IMMUNITY = noKnockback; the rest by Noita's own effect
  where our runtime has it.
- ShotEffectComponent.extra_modifier: our casting runs Noita's `gun_extra_modifiers.lua` entry on each cast (as
  Noita's gun.lua does), so LOWER_SPREAD, FAST_PROJECTILES, BOUNCE... use Noita's numbers.
- Stacking, `stackable_maximum`, `max_in_perk_pool`, `not_in_default_perk_pool` come from perk_list.lua; a drop picks from
  the default pool.
- usable_by_enemies (`func_enemy`): Noita gives these to some creatures; later, not part of PC-34.

## Not like Noita (adapted: the meaning is missing in Terraria)
Holy Mountains, shops, gold nuggets, levitation, fog of war, worms and gods have no Terraria twin. The last column says
what such a perk does instead; "as Noita" = Noita's own code and numbers. Left out of the drop pool until they mean
something here: EDIT_WANDS_EVERYWHERE, PEACE_WITH_GODS, ABILITY_ACTIONS_MATERIALIZED (and MAP, MOON_RADAR, as in Noita).

## Perks
how: E = game_effect, F = func sets player components, L = Lua script on the player, S = shot modifier,
X = child entity.

| id | name | Noita says | Noita does (perk_list.lua) | how | in Terranoita |
|---|---|---|---|---|---|
| CRITICAL_HIT | Critical Hit + | You get more critical hits | effects CRITICAL_HIT_BOOST | E | as Noita (stackable, usable_by_enemies) |
| BREATH_UNDERWATER | Breathless | You can no longer drown, and can move in liquids with ease. | effects BREATH_UNDERWATER; sets air_in_lungs,swim_down_buoyancy_coeff,swim_drag,swim_extra_horizontal_drag,swim_idle_buoya | E F | as Noita (stackable, usable_by_enemies) |
| EXTRA_MONEY | Greed | You gain double the gold per nugget. | effects EXTRA_MONEY | E | coins from Noita creatures x2 (our noita_gold drop is the gold nugget) (stackable) |
| EXTRA_MONEY_TRICK_KILL | Trick Greed | 4x instead of 2x gold is dropped when death is an accident. | effects EXTRA_MONEY_TRICK_KILL | E | coins x4 when the creature was not killed by the player (another creature, liquid, fall, own blast) (stackable) |
| GOLD_IS_FOREVER | Gold Is Forever | Gold nuggets never disappear. | sets perk_gold_is_forever | F | Terraria coins never vanish: instead no coins are lost on death |
| TRICK_BLOOD_MONEY | Trick Blood Money | Blood money is dropped when death is an accident. | sets perk_trick_kills_blood_money | F | trick kills drop a Heart item (blood money heals in Noita) |
| EXPLODING_GOLD | Exploding Gold | Gold dropped by enemies explodes when it disappears, is picked up or touched by other enemies! | func only | F | coins dropped by Noita creatures explode (Noita's gold explosion) when picked up or touched by a creature (stackable) |
| HOVER_BOOST | Strong Levitation | You can fly 100% longer. | effects HOVER_BOOST | E | Terraria flight time x2 (Noita levitation = Terraria flight) (stackable) |
| FASTER_LEVITATION | Faster Levitation | You levitate 75% faster. | effects FASTER_LEVITATION; sets pixel_gravity | E F | Terraria flight speed +75% (stackable) |
| MOVEMENT_FASTER | Faster Movement | Your movement speed is increased. | effects MOVEMENT_FASTER | E | as Noita (stackable, usable_by_enemies) |
| STRONG_KICK | Never Skip Leg Day | Your kicks deal extra damage and knockback. | sets kick_entities,telekinesis_throw_speed,throw_speed | F | as Noita (stackable) |
| TELEKINESIS | Telekinetic Kick | You gain new telekinetic powers. | sets can_kick,throw_speed; loads perk_telekinesis.xml | F X | kick (F) throws items/physics bodies as Noita |
| REPELLING_CAPE | Repelling Cape | Stains drop at a fast rate (when moving). | effects STAINS_DROP_FASTER | E | as Noita (stackable, usable_by_enemies) |
| EXPLODING_CORPSES | Exploding Corpses | Enemies explode upon death, but you gain immunity to explosive damage. | effects EXPLODING_CORPSE_SHOTS,PROTECTION_EXPLOSION | E | as Noita |
| SAVING_GRACE | Saving Grace | If you would die and have more than 1 HP, your HP is set to 1 instead. | effects SAVING_GRACE | E | as Noita (usable_by_enemies) |
| INVISIBILITY | Invisibility | You're invisible. Stains, casting spells, kicking and taking damage makes you temporarily visible. | effects INVISIBILITY; sets alpha | E F | as Noita (usable_by_enemies) |
| GLOBAL_GORE | More Blood | Blood blood blood. | effects GLOBAL_GORE | E | Noita creatures bleed more (Fluids blood); cosmetic (stackable) |
| REMOVE_FOG_OF_WAR | All-Seeing Eye | You can see everywhere. | effects REMOVE_FOG_OF_WAR | E | reveals the Terraria map in a wide radius around the player as you move |
| LEVITATION_TRAIL | Levitation Trail | When levitating, you leave a trail of magical sparks that harm passing creatures. | loads levitation_trail.lua | L | Noita's script while the player flies (wings, rocket boots) (stackable) |
| VAMPIRISM | Vampirism | You lose 25% maximum health, but can replenish health by drinking blood.\nBlood doesn't affect satia | effects HEALING_BLOOD,PROTECTION_FOOD_POISONING; sets hp,max_hp | E F | as Noita |
| EXTRA_HP | Extra Health (One-off) | You gain 50% extra maximum health. | sets hp,max_hp | F | as Noita (one_off_effect, stackable, usable_by_enemies) |
| HEARTS_MORE_EXTRA_HP | Stronger Hearts | Hearts bestow more maximum health. | func only | F | Life Crystals / Life Fruit give 50% more max life (stackable) |
| GLASS_CANNON | Glass Cannon | Your spells are 5 times as powerful, but your maximum health becomes 50 and cannot be increased by n | effects DAMAGE_MULTIPLIER; sets hp,max_hp,max_hp_cap; loads glass_cannon_enemy.lua | E F L | as Noita (stackable, usable_by_enemies) |
| LOW_HP_DAMAGE_BOOST | Living on the Edge | Your spells deal 3x damage when you're under 25 HP or 25% of maximum health. | effects LOW_HP_DAMAGE_BOOST; loads low_hp_damage_boost_enemy.lua | E L | as Noita (stackable, usable_by_enemies) |
| RESPAWN | Extra Life (One-off) | Upon death you respawn with 100 health. | effects RESPAWN | E | one revive: on death the player stands up at the spot with 100 Noita hp (one_off_effect, stackable) |
| WORM_ATTRACTOR | Worm Attractor | Worms find you attractive. | effects WORM_ATTRACTOR | E | Noita worms and Terraria worm enemies spawn more and hunt the player (stackable, usable_by_enemies) |
| RADAR_ENEMY | Enemy Radar | You can sense nearby enemies. | loads radar.lua | L | as Noita |
| FOOD_CLOCK | Eat Your Vegetables | You inflict more damage the more satiated you are, but you start losing health if your stomach is em | loads food_clock.lua,poof_white_appear.xml,potion_porridge.xml | L S X | Terraria's Well Fed is the stomach: more damage while fed, slow life loss without it |
| IRON_STOMACH | Iron Stomach | You no longer suffer from negative effects of eating. | effects IRON_STOMACH | E | immune to Terraria food/drink debuffs (Tipsy, Stinky); Potion Sickness shorter |
| WAND_RADAR | Wand Radar | You can sense nearby wands. | loads radar_wand.lua | L | as Noita |
| ITEM_RADAR | Item Radar | You can sense nearby items. | loads radar_item.lua | L | as Noita |
| MOON_RADAR | Moon Radar | You can sense lunar energy. | loads radar_moon.lua | L | arrow to the nearest WorldLoot altar (no moons); not in the drop pool, as in Noita (not_in_default_perk_pool) |
| MAP | Spatial Awareness | If you stop for a moment, you can sense your location relative to the Mountain. | loads map.xml | X | reveals the whole world map once (no Mountain in Terraria); not in the drop pool, as in Noita (not_in_default_perk_pool) |
| PROTECTION_FIRE | Fire Immunity | You take no damage from fire. | effects PROTECTION_FIRE | E | as Noita (usable_by_enemies) |
| PROTECTION_RADIOACTIVITY | Toxic Immunity | You take no damage from toxic sludge and other toxic things. | effects PROTECTION_RADIOACTIVITY | E | as Noita (usable_by_enemies) |
| PROTECTION_EXPLOSION | Explosion Immunity | You take no direct damage from explosions. | effects PROTECTION_EXPLOSION | E | as Noita (usable_by_enemies) |
| PROTECTION_MELEE | Melee Immunity | You take no damage from close-range enemy attacks. | effects PROTECTION_MELEE | E | as Noita (usable_by_enemies) |
| PROTECTION_ELECTRICITY | Electricity Immunity | You take no damage from electric shocks. | effects PROTECTION_ELECTRICITY | E | as Noita (usable_by_enemies) |
| TELEPORTITIS | Teleportitis | You take 20% less damage. You teleport away every time you're hurt. | effects TELEPORTITIS | E | as Noita (usable_by_enemies) |
| TELEPORTITIS_DODGE | Teleportitis Dodge | You teleport a short distance away when an enemy projectile is near. | loads teleportitis_dodge.xml | X | as Noita |
| STAINLESS_ARMOUR | Stainless Armour | You take 50% less damage as long as you have no active stain status effect. | effects STAINLESS_ARMOUR | E | as Noita (stackable, usable_by_enemies) |
| EDIT_WANDS_EVERYWHERE | Tinker With Wands Everywhere | A divine blessing allows you to tinker with wands everywhere. | effects EDIT_WANDS_EVERYWHERE | E | the wand window (U) already works everywhere: left out of the drop pool |
| NO_WAND_EDITING | No Wand Tinkering | Wands cannot be tinkered with, but enemies may drop blood money. | effects NO_WAND_EDITING; sets perk_hp_drop_chance | E F | the wand window is locked; Noita creatures drop Hearts more often (Noita: blood money) |
| WAND_EXPERIMENTER | Wand Experimenter | Firing newly found and unmodified wands heals you. | loads wand_experimenter.lua | L | as Noita (stackable) |
| ADVENTURER | Healthy Exploration | Every time you visit a new area, you regain 60 health. | loads adventurer.lua | L | heal 60 Noita hp on entering a Terraria biome for the first time (saved with the character) |
| ABILITY_ACTIONS_MATERIALIZED | Bombs Materialized | Bomb-like spells can be placed in the ITEMS space in inventory and used like throwable items. | effects ABILITY_ACTIONS_MATERIALIZED | E | left out of the pool until spell items can be thrown (no Noita items space) |
| PROJECTILE_HOMING | Homing Shots | Your spells home towards enemies very slightly. | effects PROJECTILE_HOMING; loads projectile_homing_enemy.lua | E L | as Noita (usable_by_enemies) |
| PROJECTILE_HOMING_SHOOTER | Boomerang Spells | Your spells arc towards you, but gain extra speed and deal extra damage. | loads projectile_homing_shooter_enemy.lua | L S | as Noita (usable_by_enemies) |
| UNLIMITED_SPELLS | Unlimited Spells | Most spells are now unlimited. | sets mActualActiveItem,perk_infinite_spells | F | as Noita |
| FREEZE_FIELD | Freeze Field | Liquids freeze in your presence. | effects PROTECTION_FIRE; loads freeze_field.xml | E X | as Noita (usable_by_enemies) |
| FIRE_GAS | Gas fire | Gases near you ignite automatically. | loads fire_gas.xml | X | as Noita (usable_by_enemies) |
| DISSOLVE_POWDERS | Dissolve Powders | Sand and other soft, powdery materials dissolve quickly in your presence. | loads dissolve_powders.xml | X | as Noita (usable_by_enemies) |
| BLEED_SLIME | Slime Blood | You bleed slime, but slime no longer slows you down and you have higher projectile resistance. | effects NO_SLIME_SLOWDOWN; sets blood_material,blood_multiplier,blood_spray_material,blood_sprite_directional,blood_sprite; loads bloodsplatter_directional_purple_$[1-3].xml,bloodsplatter_purple_$[1-3].xml | E F X | as Noita (stackable, usable_by_enemies) |
| BLEED_OIL | Oil Blood | You bleed flammable oil, but are immune to fire. | effects PROTECTION_FIRE; sets blood_material,blood_multiplier,blood_spray_material,blood_sprite_directional,blood_sprite; loads bloodsplatter_directional_oil_$[1-3].xml,bloodsplatter_oil_$[1-3].xml | E F X | as Noita (usable_by_enemies) |
| BLEED_GAS | Gas Blood | You bleed flammable gas instead of blood. | effects PROTECTION_RADIOACTIVITY; sets blood_material,blood_multiplier,blood_spray_material,blood_sprite_directional,blood_sprite; loads bloodsplatter_directional_green_$[1-3].xml,bloodsplatter_green_$[1-3].xml | E F X | as Noita (usable_by_enemies) |
| SHIELD | Permanent Shield | You gain a small, permanent shield. | sets area_circle_radius,radius,recharge_speed; loads shield.xml | F X | as Noita (stackable, usable_by_enemies) |
| REVENGE_EXPLOSION | Revenge Explosion | You release a magical explosion upon taking damage, and gain 25% resistance against explosions. | loads revenge_explosion.lua | L | as Noita (stackable, usable_by_enemies) |
| REVENGE_TENTACLE | Revenge Tentacle | You summon a monstrous tentacle upon taking damage, and gain 25% resistance against projectiles. | loads revenge_tentacle.lua | L | as Noita (stackable, usable_by_enemies) |
| REVENGE_RATS | Revenge Rats | When you take damage, there's a chance that a helpful rat minion is summoned | loads revenge_rats.lua | L | as Noita |
| REVENGE_BULLET | Revenge bullets | You return fire when hit by projectiles, and you gain 20% resistance against explosions and projecti | loads revenge_bullet.lua | L | as Noita (stackable, usable_by_enemies) |
| ATTACK_FOOT | Lukki Mutation | You grow curious additional limbs that fight for you. | sets pixel_gravity; loads limb_attacker.xml,limb_climb.xml,limb_walker.xml | F X | as Noita (stackable, usable_by_enemies) |
| LEGGY_FEET | Leggy Mutation | You grow disturbing looking limbs that fight for you. | loads leggy_limb_attacker.xml,leggy_limb_left.xml,leggy_limb_right.xml,limb_climb.xml | X | as Noita (not_in_default_perk_pool, stackable, usable_by_enemies) |
| PLAGUE_RATS | Plague Rats | Dying enemies release rats to serve your bidding! All rats become your friend. | loads plague_rats.lua | L | as Noita (stackable) |
| VOMIT_RATS | Spontaneous Generation | Vomit near you evolves into helpful rat minions | loads poof_white_appear.xml,potion_vomit.xml,vomit_rats.xml | X | as Noita |
| CORDYCEPS | Cordyceps | Fungal creatures spawn from the corpses of enemies killed by you. | loads cordyceps.lua | L | as Noita |
| MOLD | Fungal Colony | Slime near you spontaneously turns into fungal creatures. | loads poof_white_appear.xml,potion_slime.xml,slime_fungus.xml | X | as Noita |
| WORM_SMALLER_HOLES | Feared by Worms | Worms run away from you, and worm and lukki enemies no longer destroy terrain while burrowing. | effects WORM_DETRACTOR; loads worm_smaller_holes.lua | E L | Noita worms flee; their digging does not break blocks |
| PROJECTILE_REPULSION | Projectile Repulsion Field | Most projectiles are repulsed by your presence, but you take slightly more projectile damage. | loads projectile_repulsion_field.xml | X | as Noita (stackable, usable_by_enemies) |
| RISKY_CRITICAL | Close Call | You gain additional chance to deal critical hits as long as there are enemies near you. | loads risky_critical.xml | X | as Noita (stackable) |
| FUNGAL_DISEASE | Fungal Disease | When near danger, you sprout fungal growths. | loads fungal_disease.xml | X | as Noita (stackable) |
| PROJECTILE_SLOW_FIELD | Projectile Slower | Projectiles near you slow down. | loads projectile_slow_field.xml | X | as Noita (stackable, usable_by_enemies) |
| PROJECTILE_REPULSION_SECTOR | Projectile Repulsion Sector | Projectiles that fly into a small sector in front of you get blown away. | loads projectile_repulsion_sector.xml | X | as Noita (stackable) |
| PROJECTILE_EATER_SECTOR | Projectile Eater | Projectiles that fly into a small sector behind of you disappear. | loads projectile_eater_sector.xml,projectile_eater_sector_noparticles.xml | X | as Noita |
| ORBIT | Phasing | Projectiles seemingly phase through you. | loads orbit.lua | L | as Noita (stackable, usable_by_enemies) |
| ANGRY_GHOST | Angry ghost | An angry spirit comes to your aid, copying nearby spells and projectiles. | loads angry_ghost.xml,angry_ghost_shoot.lua | L X | as Noita (stackable, usable_by_enemies) |
| HUNGRY_GHOST | Hungry Ghost | Summons a happy minion who'll eat enemy projectile every now and then. | loads hungry_ghost.xml | X | as Noita (stackable, usable_by_enemies) |
| DEATH_GHOST | Mournful Spirit | Dying creatures leave behind a spirit that deals damage in a small area. | loads death_ghost.lua | L | as Noita (stackable) |
| HOMUNCULUS | Homunculus | Every time you leave a Holy Mountain, a helpful homunculus is summoned. | loads homunculus_spawner.xml | X | as Noita (stackable) |
| LUKKI_MINION | Lukki Minion | Summons a lukki minion to your help. | loads lukki_minion.xml | X | as Noita |
| ELECTRICITY | Electricity | You're immune to electric damage, but metal and liquids around you electrify constantly. Look out! | effects PROTECTION_ELECTRICITY; loads electricity.xml | E X | as Noita (usable_by_enemies) |
| ATTRACT_ITEMS | Attract Gold | Gold nuggets gravitate towards you. | loads attract_items.lua,attract_items_enemy.lua | L | as Noita (stackable) |
| EXTRA_KNOCKBACK | Extra Knockback on Spells | Your spells knock enemies around with more force. | loads extra_knockback_enemy.lua | L S | as Noita (stackable, usable_by_enemies) |
| LOWER_SPREAD | Concentrated Spells | Your spells have lower spread and extra damage, but have increased cast delay. | loads lower_spread_enemy.lua | L S | as Noita (stackable, usable_by_enemies) |
| LOW_RECOIL | Low Recoil | Recoil caused by your spells is greatly reduced, but your spells fly slightly slower. | func only | S | as Noita |
| BOUNCE | Bouncing Spells | Almost all your spells bounce around and last longer. | loads bounce_enemy.lua | L S | as Noita (usable_by_enemies) |
| FAST_PROJECTILES | Faster Projectiles | Your projectile spells fly faster than before. | loads fast_projectiles_enemy.lua | L S | as Noita (usable_by_enemies) |
| ALWAYS_CAST | Always Cast (One-off) | A random Always Cast spell is added to the wand in your hand, up to a maximum of 4. | func only | F | as Noita, on the wand in the hand (WandStore), max 4 (one_off_effect, stackable) |
| EXTRA_MANA | High Mana, Low Capacity (One-off) | Your currently held wand loses half its capacity, but gains more mana. | sets mana_charge_speed,mana_max | F | as Noita, on the held wand (WandStore) (one_off_effect, stackable) |
| NO_MORE_SHUFFLE | No More Shuffle | Most of the wands will be non shuffling. The wands you carry with you turn into non-shufflers too. | func only | F | as Noita: carried wands and new ones |
| NO_MORE_KNOCKBACK | No More Knockback | Enemies can no longer knock you back. | effects KNOCKBACK_IMMUNITY | E | as Noita |
| DUPLICATE_PROJECTILE | Projectile duplication | Your projectile spells have a chance to duplicate, but you're more vulnerable to projectile damage | sets attack_ranged_entity_count_max | F S | as Noita (stackable, usable_by_enemies) |
| FASTER_WANDS | Faster Wands (One-off) | All wands you're currently carrying gain a bonus to their cast delay & reload time. | sets mana_charge_speed | F | as Noita, on carried wands (one_off_effect, stackable) |
| EXTRA_SLOTS | Extra Wand Capacity (One-off) | The wands you're currently carrying gain 1-3 additional spell slots (to a maximum of 25). | func only | F | as Noita, on carried wands (max 25; the wand window shows 16: widen it) (one_off_effect, stackable) |
| CONTACT_DAMAGE | Contact Damage | Enemies near you take damage, the damage is higher the lower your health gets. | loads contact_damage.xml,contact_damage_enemy.xml | X | as Noita (usable_by_enemies) |
| EXTRA_PERK | Extra Perk | From now on you will find an extra perk in every Holy Mountain. | func only | F | every boss drops one more perk item (stackable) |
| PERKS_LOTTERY | Perk Lottery | When you pick a perk, there's a 50% chance the others won't disappear. | func only | F | 50%: a used perk item is not consumed (Noita: the other perks stay) (stackable) |
| GAMBLE | Gamble (One-off) | You gain two random perks. | loads perk_gamble_spawner.xml | X | applies two random perks at once (Noita's gamble picks them) (one_off_effect, stackable) |
| EXTRA_SHOP_ITEM | Extra Item In Holy Mountain | There will be an additional item in every Holy Mountain you haven't discovered yet. | func only | F | no Holy Mountain: the next Terraria boss also drops a random wand (stackable) |
| GENOME_MORE_HATRED | More Hatred | Creatures become more aggressive towards each other. | sets global_genome_relations_modifier | F | Noita creatures fight each other more (Noita's global_genome_relations_modifier in our herd relations) (stackable) |
| GENOME_MORE_LOVE | More Love | Creatures become more friendly towards each other. | sets global_genome_relations_modifier | F | Noita creatures fight each other less (same field) (stackable) |
| PEACE_WITH_GODS | Peace with Gods | You make peace with your Gods. | func only | F | no gods/Steve in Terraria: left out of the drop pool |
| MANA_FROM_KILLS | Kills to mana | Every time an enemy near you dies, you release mana-recharging liquid. | loads mana_from_kills.lua | L | as Noita |
| ANGRY_LEVITATION | Rage-fueled Levitation | Killing an enemy makes you replenish some of your levitation power. | loads angry_levitation.lua | L | kills refill Terraria flight time (wings, rocket boots) |
| LASER_AIM | Pinpointer | Your spells fly faster and have lower spread, and you have a handy sightline for aiming! | loads laser_aim.xml | S X | as Noita (stackable) |
| PERSONAL_LASER | Personal Plasma Beam | You constantly fire a devastating plasma beam, but you fire spells much slower | loads personal_laser.xml | S X | as Noita (stackable) |
| MEGA_BEAM_STONE | Summon Sädekivi (One-off) | You gain an artefact that allows you to call the celestial rage. | loads beamstone.xml,poof_white_appear.xml | X | Noita's beamstone as an item (until throwables exist: a wand with its spell) (one_off_effect, stackable) |

## Order of work
1. Core: perk list reader (perk_list.lua in MoonSharp: ids, flags, funcs) + tests; the player store entity with the
   components above.
2. Game: perk item (one item type, prefix = perk number, like spell items), boss drop (count by difficulty), use ->
   func, icon row (Noita's perk_icon), save per character (side file like WandStore), death rules.
3. E, F and S perks first (most of them), then L and X (they need more components: EnergyShield, fields, ghosts).

Check (author): kill a boss in a test world -> perk items drop; use one -> effect + icon; die -> perks gone; with more
than 10, a quarter drop.
