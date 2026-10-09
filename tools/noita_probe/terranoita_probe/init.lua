-- Terranoita probe (design/magic_plan.md PC-22): the ground truth for Terranoita's magic, measured in real Noita.
-- For every test of files/tests.lua (made by `tncli probe-tests` from gun_actions.lua) it stamps a sky arena, gives the
-- player a fresh wand holding the test's deck, fires one cast at a target, watches every projectile until all are gone
-- (or MAX_FRAMES) and appends one JSON line to mods/terranoita_probe/probe_out.jsonl. Tests already in the file are
-- skipped, so a restart continues. Source: D:/terranoita/tools/noita_probe (installed by copying the folder).

dofile_once( "data/scripts/lib/utilities.lua" )
dofile_once( "data/scripts/gun/procedural/gun_action_utils.lua" )
dofile_once( "mods/terranoita_probe/files/tests.lua" )
local DIAG = {}
-- single-cast ways first (run 2026-10-09: S1 fired again and again, S2 and S3 once, S4 never)
-- run 2026-10-09 #2: only S2 cast, and only after input was back on (aimed by the real mouse). S5/S6 keep input off:
-- PlatformShooterPlayerComponent.mForceFireOnNextUpdate / mRequireTriggerPull (component_documentation.txt)
for i, k in ipairs( { 5, 6, 3, 2 } ) do DIAG[i] = { name = "diag:S" .. k, deck = { "LIGHT_BULLET" }, strategy = k } end
local RUN = {}
for _, x in ipairs( DIAG ) do RUN[#RUN + 1] = x end
for _, x in ipairs( TESTS ) do RUN[#RUN + 1] = x end

local OUT = "mods/terranoita_probe/probe_out.jsonl"
local ARENA, ARENA_W, ARENA_H = "mods/terranoita_probe/files/arena.png", 320, 200
local START_DELAY, STAMP_WAIT, MAX_FRAMES, QUIET = 120, 6, 240, 20

local player, ax, ay, px, py, tx, ty
local state, idx, t, fire_method = "wait", 0, 0, 2
local wand, target, mana0, fired_frame
local early = 0   -- projectiles that appeared before the probe pressed fire (counted, reported)
local chosen   -- the way of pressing fire that worked (diag tests), kept across a restart
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

local STATUS = "mods/terranoita_probe/probe_status.txt"
local function status( text )
	local f = io and io.open( STATUS, "a" )
	if f then f:write( tostring( GameGetFrameNum and GameGetFrameNum() or 0 ) .. " " .. text .. "\n" ) f:close() end
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
		local k = line:match( "^{\"name\":\"diag:S(%d)\"" )
		if k and chosen == nil and line:find( "\"projectiles\":%[{" ) then chosen = tonumber( k ) end
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
	ComponentSetValue2( ab, "mana_charge_speed", 0 )   -- no refill: mana_used is what the cast took
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

local diag = ""
local WAND_READY, FIRE_MAX = 6, 60   -- with mRequireTriggerPull off (S5/S6) a new wand fires on the first frame (run 2026-10-09)

local function active_item()
	local inv2 = EntityGetFirstComponent( player, "Inventory2Component" )
	return inv2 and ComponentGetValue2( inv2, "mActiveItem" ) or -1
end

-- aim at the target (every aiming field; the first run with only three of them threw a bomb up-left)
local function aim( c )
	local dx, dy = tx - px, ty - py
	local len = math.sqrt( dx * dx + dy * dy )
	ComponentSetValue2( c, "mAimingVector", dx, dy )
	ComponentSetValue2( c, "mAimingVectorNormalized", dx / len, dy / len )
	ComponentSetValue2( c, "mAimingVectorNonZeroLatest", dx, dy )
	ComponentSetValue2( c, "mMousePosition", tx, ty )
	ComponentSetValue2( c, "mGamePadCursorInWorld", tx, ty )
end

-- pressing fire from Lua is not documented, so the probe first tries ways on LIGHT_BULLET (diag tests) and keeps the
-- first that fires. Seen 2026-10-09: mButtonDownFire alone is overwritten by the input (read back false); a bomb fired
-- 12 frames after "input off + pressed 2 frames, then input on again" (S2), but aimed by the real mouse.
-- S1 input on, button set in pre and post update; S2 that sequence; S3 input off, button toggled every frame; S4 input
-- off, button held. fire_t: frames since this test started pressing.
local strategy, fire_t = 2, 0
local function press( down )
	local c = EntityGetFirstComponent( player, "ControlsComponent" )
	if not c then return end
	if not down then
		-- run 2026-10-09: with input on while waiting, the new wand cast by itself ~27 frames after pickup, aimed by
		-- the real mouse: input stays off and aimed whenever the probe is not pressing
		ComponentSetValue2( c, "enabled", false )
		aim( c )
		ComponentSetValue2( c, "mButtonDownFire", false )
		return
	end
	local frame = GameGetFrameNum()
	if strategy == 1 then
		ComponentSetValue2( c, "enabled", true )
		aim( c )
		ComponentSetValue2( c, "mButtonDownFire", true )
		ComponentSetValue2( c, "mButtonFrameFire", frame )
	elseif strategy == 2 then
		local on = fire_t <= 1
		ComponentSetValue2( c, "enabled", not on )
		aim( c )
		ComponentSetValue2( c, "mButtonDownFire", on )
		if on then ComponentSetValue2( c, "mButtonFrameFire", frame ) end
	elseif strategy == 3 then
		ComponentSetValue2( c, "enabled", false )
		aim( c )
		local on = fire_t % 2 == 0
		ComponentSetValue2( c, "mButtonDownFire", on )
		if on then ComponentSetValue2( c, "mButtonFrameFire", frame ) end
	elseif strategy == 4 then
		ComponentSetValue2( c, "enabled", false )
		aim( c )
		ComponentSetValue2( c, "mButtonDownFire", true )
		if fire_t == 0 then ComponentSetValue2( c, "mButtonFrameFire", frame ) end
	else
		ComponentSetValue2( c, "enabled", false )
		aim( c )
		local ps = EntityGetFirstComponent( player, "PlatformShooterPlayerComponent" )
		if ps then
			ComponentSetValue2( ps, "mRequireTriggerPull", false )
			if strategy == 5 then ComponentSetValue2( ps, "mForceFireOnNextUpdate", true ) end
		end
		ComponentSetValue2( c, "mButtonDownFire", true )
		ComponentSetValue2( c, "mButtonFrameFire", frame )
	end
end

local function release_controls()
	local c = EntityGetFirstComponent( player, "ControlsComponent" )
	if c then
		ComponentSetValue2( c, "mButtonDownFire", false )
		ComponentSetValue2( c, "enabled", true )
	end
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
	repeat idx = idx + 1 until idx > #RUN or not done[RUN[idx].name]
	if idx > #RUN then
		out( "{\"done\":true,\"tests\":" .. #TESTS .. "}" )
		GamePrint( "Terranoita probe: all " .. #TESTS .. " tests written to " .. OUT )
		state = "done"
		return
	end
	if idx % 25 == 0 then GamePrint( "Terranoita probe: test " .. idx .. " of " .. #TESTS ) end
	state, t, diag = "stamp", 0, ""
end

function OnModInit()
	status( "mod loaded, io " .. tostring( io ~= nil ) .. ", " .. #TESTS .. " tests" )
end

function OnPlayerSpawned( player_entity )
	player = player_entity
	status( "player spawned " .. tostring( player_entity ) )
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
	if state == "ready" or state == "watch" then press( false )   -- aimed, not firing (one cast per test)
	elseif state == "fire" then
		press( #order == 0 and not ( mana0 and wand_mana() and wand_mana() < mana0 ) )
		fire_t = fire_t + 1
	end
end

local update   -- the probe step; errors are written to probe_status.txt (no screenshots: the author's rule)
function OnWorldPostUpdate()
	local ok, err = pcall( update )
	if not ok then
		status( "ERROR " .. tostring( err ) )
		GamePrint( "Terranoita probe error: " .. tostring( err ) )
		state = "done"
	end
end

update = function()
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
			wand, ab = make_wand( RUN[idx].deck )
			tracked, order, spawned_frame = {}, {}, 0
			early = 0
		elseif t == STAMP_WAIT + 1 then
			state, t = "ready", 0
		end
	elseif state == "ready" then
		if track( t - WAND_READY ) > 0 or #order > 0 then early = #order end
		if t >= WAND_READY then
			mana0 = wand_mana()
			diag = "active " .. tostring( active_item() ) .. " wand " .. tostring( wand ) .. " items " .. #( GameGetAllInventoryItems( player ) or {} )
			if RUN[idx].strategy then
				strategy = RUN[idx].strategy
			elseif chosen then
				strategy = chosen
			else
				status( "no way of pressing fire worked (diag S1-S4): stopped" )
				GamePrint( "Terranoita probe: firing failed, see " .. STATUS )
				state = "done"
				return
			end
			diag = "S" .. strategy .. "; early projectiles " .. early .. "; " .. diag
			fire_t = 0
			state, t = "fire", 0
		end
	elseif state == "fire" then
		track( t )
		local fired = #order > 0 or ( mana0 and wand_mana() and wand_mana() < mana0 )
		if t == 1 or fired then
			local c = EntityGetFirstComponent( player, "ControlsComponent" )
			local x, y = EntityGetTransform( player )
			local avx, avy = 0, 0
			if c then avx, avy = ComponentGetValue2( c, "mAimingVector" ) end
			local wx, wy = 0, 0
			if wand and EntityGetIsAlive( wand ) then wx, wy = EntityGetTransform( wand ) end
			diag = diag .. string.format( "; t%d player at %.1f,%.1f wand at %.1f,%.1f aim read %.1f,%.1f", t, x - px, y - py, wx - px, wy - py, avx, avy )
		end
		if fired then
			fired_frame = t
			if RUN[idx].strategy and not chosen then
				chosen = RUN[idx].strategy
				status( "fire strategy S" .. chosen .. " works" )
			end
			state = "watch"
		elseif t >= FIRE_MAX then
			release_controls()
			finish( RUN[idx], t, "nothing fired in " .. FIRE_MAX .. " frames (no projectile, no mana used); " .. diag )
			if idx == 1 then GamePrint( "Terranoita probe: firing failed, see " .. OUT ) end
			next_test()
		end
	elseif state == "watch" then
		local alive = track( t )
		if t >= MAX_FRAMES or ( alive == 0 and t - spawned_frame >= QUIET and t - fired_frame >= QUIET ) then
			release_controls()
			finish( RUN[idx], t, ( alive > 0 and "still flying at the end; " or "" ) .. "fired at frame " .. tostring( fired_frame ) ..
				( ( RUN[idx].strategy or idx <= 6 ) and ( "; " .. diag ) or "" ) )
			next_test()
		end
	end
end
