-- LuaComponent script_shot on the probe's player: Noita calls shot() for every projectile the player shoots, even one
-- that is gone before the probe's next look (component_documentation.txt: "when we receive Message_Shot")
function shot( projectile_entity_id )
	GlobalsSetValue( "tnprobe_shots", GlobalsGetValue( "tnprobe_shots", "" ) .. tostring( projectile_entity_id ) .. "|" .. ( EntityGetFilename( projectile_entity_id ) or "" ) .. "\n" )
end
