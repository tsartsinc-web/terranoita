-- LuaComponent script_damage_received of the probe target: each hit appended to a global the probe reads
function damage_received( damage, message, entity_thats_responsible, is_fatal, projectile_thats_responsible )
	local by = ""
	if projectile_thats_responsible ~= nil and projectile_thats_responsible ~= 0 then
		by = EntityGetFilename( projectile_thats_responsible ) or ""
	end
	GlobalsSetValue( "tnprobe_hits", GlobalsGetValue( "tnprobe_hits", "" ) .. tostring( damage ) .. "|" .. tostring( message ) .. "|" .. by .. "\n" )
end
