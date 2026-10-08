-- Terranoita probe (design/magic_plan.md PC-22): the ground truth for Terranoita's magic, measured in real Noita.
-- For every test of files/tests.lua (made by `tncli probe-tests` from gun_actions.lua) it stamps a sky arena, gives the
-- player a fresh wand holding the test's deck, fires one cast at a target, watches every projectile until all are gone
-- (or MAX_FRAMES) and appends one JSON line to mods/terranoita_probe/probe_out.jsonl. Tests already in the file are
-- skipped, so a restart continues. Source: D:/terranoita/tools/noita_probe (installed by copying the folder).

dofile_once( "data/scripts/lib/utilities.lua" )
dofile_once( "data/scripts/gun/procedural/gun_action_utils.lua" )
dofile_once( "mods/terranoita_probe/files/tests.lua" )

local OUT = "mods/terranoita_probe/probe_out.jsonl"
local ARENA, ARENA_W, ARENA_H = "mods/terranoita_probe/files/arena.png", 320, 200
local START_DELAY, STAMP_WAIT, MAX_FRAMES, QUIET = 120, 15, 240, 30

local player, ax, ay, px, py, tx, ty
local state, idx, t, fire_method = "wait", 0, 0, 1
local wand, target, mana0, fired_frame
local tracked, order, done = {}, {}, {}
local spawned_frame = 0

local function q( s )
	s = tostring( s or "" )
	s = s:gsub( "\\", "\\\\" ):gsub( "\"", "\\\"" ):gsub( "[\r\n\t]", " " )
	return "\"" .. s .. "\""
end
local function n( x )
	if x == nil or x ~= x then return "null" end
	return string.format( "%.2f", x )
end

local function out( line )
	local f = io.open( OUT, "a" )
	if f then f:write( line .. "\n" ) f:close() end
end

local function read_done()
	local f = io.open( OUT, "r" )
	if not f then return end
	for line in f:lines() do
		local name = line:match( "^{\"name\":\"([^\"]+)\"" )
		if name then done[name] = true end
	end
	f:close()
end

local function quick_inventory()
	for _, c in ipairs( EntityGetAllChildren( player ) or {} ) do
		if EntityGetName( c ) == "inventory_quick" then return c end
	end
end

local function clear_arena()
	-- every root entity near the arena except the player: projectiles, summons, drops, the old target
	for _, e in ipairs( EntityGetInRadius( ax + ARENA_W / 2, ay + ARENA_H / 2, 700 ) or {} ) do
		if e ~= player and EntityGetParent( e ) == 0 and EntityGetRootEntity( e ) ~= player then
			EntityKill( e )
		end
	end
	local inv = quick_inventory()
	for _, item in ipairs( inv and EntityGetAllChildren( inv ) or {} ) do
		GameKillInventoryItem( player, item )
	end
end

local function pin_player()
	EntitySetTransform( player, px, py )
	local cd = EntityGetFirstComponent( player, "CharacterDataComponent" )
	if cd then ComponentSetValue2( cd, "mVelocity", 0, 0 ) end
	local dm = EntityGetFirstComponent( player, "DamageModelComponent" )
	if dm then
		local max_hp = ComponentGetValue2( dm, "max_hp" )
		ComponentSetValue2( dm, "hp", max_hp )
	end
end

local function make_wand( deck )
	local w = EntityLoad( "data/entities/_debug/testwand.xml", px, py )
	local ab = EntityGetFirstComponentIncludingDisabled( w, "AbilityComponent" )
	ComponentSetValue2( ab, "mana_max", 100000 )
	ComponentSetValue2( ab, "mana", 100000 )
	ComponentSetValue2( ab, "mana_charge_speed", 100000 )
	ComponentObjectSetValue2( ab, "gun_config", "actions_per_round", 1 )
	ComponentObjectSetValue2( ab, "gun_config", "deck_capacity", #deck )
	ComponentObjectSetValue2( ab, "gun_config", "reload_time", 30 )
	ComponentObjectSetValue2( ab, "gun_config", "shuffle_deck_when_empty", false )
	ComponentObjectSetValue2( ab, "gunaction_config", "fire_rate_wait", 10 )
	ComponentObjectSetValue2( ab, "gunaction_config", "spread_degrees", 0 )
	ComponentObjectSetValue2( ab, "gunaction_config", "speed_multiplier", 1 )
	for _, id in ipairs( deck ) do AddGunAction( w, id ) end
	GamePickUpInventoryItem( player, w, false )
	local inv2 = EntityGetFirstComponent( player, "Inventory2Component" )
	if inv2 then ComponentSetValue2( inv2, "mForceRefresh", true ) end
	return w, ab
end

local function wand_mana()
	local ab = wand and EntityGetIsAlive( wand ) and EntityGetFirstComponentIncludingDisabled( wand, "AbilityComponent" )
	return ab and ComponentGetValue2( ab, "mana" ) or nil
end

local function press( down )
	local c = EntityGetFirstComponent( player, "ControlsComponent" )
	if not c then return end
	-- method 1: set the fields; method 2: also disable input so the engine keeps them (assumed; the row says which worked)
	if fire_method == 2 then ComponentSetValue2( c, "enabled", not down ) end
	ComponentSetValue2( c, "mButtonDownFire", down )
	if down then ComponentSetValue2( c, "mButtonFrameFire", GameGetFrameNum() ) end
	ComponentSetValue2( c, "mAimingVector", tx - px, ty - py )
	ComponentSetValue2( c, "mAimingVectorNormalized", 1, 0 )
	ComponentSetValue2( c, "mMousePosition", tx, ty )
end

local function track( frame )
	for _, e in ipairs( EntityGetWithTag( "projectile" ) or {} ) do
		if not tracked[e] then
			local x, y = EntityGetTransform( e )
			local vc = EntityGetFirstComponent( e, "VelocityComponent" )
			local pc = EntityGetFirstComponent( e, "ProjectileComponent" )
			local vx, vy = 0, 0
			if vc then vx, vy = ComponentGetValue2( vc, "mVelocity" ) end
			local parent = pc and ComponentGetValue2( pc, "mEntityThatShot" ) or 0
			tracked[e] = { file = EntityGetFilename( e ), born = frame, x0 = x - px, y0 = y - py, vx0 = vx, vy0 = vy,
				parent = tracked[parent] and tracked[parent].file or ( parent ~= 0 and EntityGetFilename( parent ) or "" ),
				lifetime = pc and ComponentGetValue2( pc, "lifetime" ) or nil, x1 = x - px, y1 = y - py, last = frame }
			order[#order + 1] = e
			spawned_frame = frame
		end
	end
	local alive = 0
	for _, e in ipairs( order ) do
		local r = tracked[e]
		if r.dead == nil then
			if EntityGetIsAlive( e ) then
				alive = alive + 1
				local x, y = EntityGetTransform( e )
				r.x1, r.y1, r.last = x - px, y - py, frame
				if frame == r.born + 1 then
					local vc = EntityGetFirstComponent( e, "VelocityComponent" )
					if vc then r.vx1, r.vy1 = ComponentGetValue2( vc, "mVelocity" ) end
				end
			else
				r.dead = frame
			end
		end
	end
	return alive
end

local function finish( test, frame, note )
	local parts = {}
	for _, e in ipairs( order ) do
		local r = tracked[e]
		parts[#parts + 1] = "{\"file\":" .. q( r.file ) .. ",\"parent\":" .. q( r.parent ) .. ",\"born\":" .. r.born ..
			",\"end\":" .. ( r.dead and tostring( r.dead ) or "null" ) .. ",\"lifetime\":" .. n( r.lifetime ) ..
			",\"x0\":" .. n( r.x0 ) .. ",\"y0\":" .. n( r.y0 ) .. ",\"vx0\":" .. n( r.vx0 ) .. ",\"vy0\":" .. n( r.vy0 ) ..
			",\"vx1\":" .. n( r.vx1 ) .. ",\"vy1\":" .. n( r.vy1 ) .. ",\"x1\":" .. n( r.x1 ) .. ",\"y1\":" .. n( r.y1 ) .. "}"
	end
	local hits = {}
	for line in ( GlobalsGetValue( "tnprobe_hits", "" ) ):gmatch( "[^\n]+" ) do
		local d, m, by = line:match( "^([^|]*)|([^|]*)|(.*)$" )
		hits[#hits + 1] = "{\"damage\":" .. n( tonumber( d ) ) .. ",\"message\":" .. q( m ) .. ",\"by\":" .. q( by ) .. "}"
	end
	local deck = {}
	for _, id in ipairs( test.deck ) do deck[#deck + 1] = q( id ) end
	local mana1 = wand_mana()
	out( "{\"name\":" .. q( test.name ) .. ",\"deck\":[" .. table.concat( deck, "," ) .. "],\"fire_method\":" .. fire_method ..
		",\"mana_used\":" .. n( mana0 and mana1 and ( mana0 - mana1 ) or nil ) .. ",\"frames\":" .. frame ..
		",\"target_alive\":" .. tostring( target ~= nil and EntityGetIsAlive( target ) ) ..
		( note and ( ",\"note\":" .. q( note ) ) or "" ) ..
		",\"projectiles\":[" .. table.concat( parts, "," ) .. "],\"hits\":[" .. table.concat( hits, "," ) .. "]}" )
end

local function next_test()
	repeat idx = idx + 1 until idx > #TESTS or not done[TESTS[idx].name]
	if idx > #TESTS then
		out( "{\"done\":true,\"tests\":" .. #TESTS .. "}" )
		GamePrint( "Terranoita probe: all " .. #TESTS .. " tests written to " .. OUT )
		state = "done"
		return
	end
	if idx % 25 == 0 then GamePrint( "Terranoita probe: test " .. idx .. " of " .. #TESTS ) end
	state, t = "stamp", 0
end

function OnPlayerSpawned( player_entity )
	player = player_entity
	if io == nil then
		GamePrint( "Terranoita probe: no file access. Enable unsafe mods in the Mods menu, then start a new game." )
		state = "done"
		return
	end
	read_done()
	local x, y = EntityGetTransform( player )
	ax, ay = x - 160, y - 700                     -- a box of air high in the sky above the start
	px, py = ax + 40, ay + ARENA_H - 20          -- the player stands on the floor at the left
	tx, ty = ax + 200, py - 4                    -- the target 160 px to the right, the wall 80 px behind it
	GetGameEffectLoadTo( player, "PROTECTION_ALL", true )
	GetGameEffectLoadTo( player, "PROTECTION_POLYMORPH", true )
	state, t = "start", 0
end

function OnWorldPreUpdate()
	if player == nil or state == "done" or state == "wait" or not EntityGetIsAlive( player ) then return end
	pin_player()
	if state == "fire" then press( t <= 1 ) end
end

function OnWorldPostUpdate()
	if player == nil or state == "done" or state == "wait" or not EntityGetIsAlive( player ) then return end
	t = t + 1
	if state == "start" then
		if t >= START_DELAY then next_test() end
	elseif state == "stamp" then
		if t == 1 then
			clear_arena()
			LoadPixelScene( ARENA, "", ax, ay, "", true, false, {}, 50, true )
			GlobalsSetValue( "tnprobe_hits", "" )
		elseif t == STAMP_WAIT then
			target = EntityLoad( "mods/terranoita_probe/files/target.xml", tx, ty )
			local ab
			wand, ab = make_wand( TESTS[idx].deck )
			tracked, order, spawned_frame = {}, {}, 0
		elseif t == STAMP_WAIT + 5 then
			mana0 = wand_mana()
			state, t = "fire", 0
		end
	elseif state == "fire" then
		local alive = track( t )
		if t >= 3 then state = "watch" end
	elseif state == "watch" then
		local alive = track( t )
		local fired = #order > 0 or ( mana0 and wand_mana() and wand_mana() < mana0 )
		if not fired and t >= 20 then
			if fire_method == 1 then
				fire_method = 2   -- the first way of pressing fire did nothing: try the other one on this test
				press( false )
				state, t = "fire", 0
				return
			end
			finish( TESTS[idx], t, "nothing fired (no projectile, no mana used)" )
			if idx == 1 or ( #order == 0 and TESTS[idx].name == "single:LIGHT_BULLET" ) then
				GamePrint( "Terranoita probe: firing failed, see " .. OUT )
			end
			next_test()
		elseif fired and ( t >= MAX_FRAMES or ( alive == 0 and t - spawned_frame >= QUIET ) ) then
			finish( TESTS[idx], t, alive > 0 and "still flying at the end" or nil )
			next_test()
		end
	end
end
