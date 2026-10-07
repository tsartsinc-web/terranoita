Terranoita, ветка `claude/dazzling-carson-h8mi9n` (сначала `git pull`). Прочитай CLAUDE.md (раздел Cloud sessions) и
последние разделы MODLOG.md. Игр здесь нет — это задача только для Core, без файлов Ноиты.

## Задача: LuaShotScripts — Noita's own per-projectile scripts in Core

Stage 3 casts spells through Noita's gun.lua (Core/Noita/LuaGun.cs). Modifier spells add `extra_entities` to a shot
(e.g. data/entities/misc/sinewave.xml, orbit_shot.xml, arc_electric.xml...): Noita entity files that become a child
of the projectile. 70 of 125 such files are LuaComponent scripts (63 scripts). The local PC session will run them as
is; you build the engine they run on (like LuaGun plays Noita's engine for gun.lua). Author's rule: Noita's own code,
not a re-implementation.

### 1. Core/Noita/NoitaEntityXml.cs — Noita entity XML -> components
- Input: path + `Func<string, string> read` (same as LuaWandMaker). Follow `<Base file="...">` chains (base first,
  the child's components/attributes override/extend; a component inside `<Base>` overrides the base's same-type
  component's attributes). Strip `<!-- -->` comments. Noita XML is sloppy: tolerate unquoted junk, duplicate attrs.
- Component = type, `_tags`, `_enabled` (default 1), fields (attribute name -> string), object fields from nested
  elements (`<ProjectileComponent><config_explosion damage="3"/></ProjectileComponent>` -> "config_explosion.damage").
- Entity: `name`, `tags`, child `<Entity>` elements (children).

### 2. Core/Noita/LuaShotScripts.cs — the runtime
- Entities: ids, parent/children, tags, transform (x, y, rot, scale_x, scale_y), components (id, type, tags, enabled,
  fields as strings; Vector2 fields stored as "x,y" or two keys, your choice). Created from NoitaEntityXml or empty.
- `IShotHost` (implemented later by the game, a fake one in tests): root shot entities are backed by the host:
  position get/set, velocity get/set (Noita px/second; VelocityComponent.mVelocity of a host entity reads/writes it),
  `GetField/SetField(entity, compType, field)` for host-owned fields (ProjectileComponent lifetime, damage,
  bounces_left, mWhoShot...) falling back to stored values, Kill(entity), InRadiusWithTag(x, y, r, tag),
  HitboxCenter(entity), Raytrace(x1, y1, x2, y2) -> (hit, x, y), Load(file, x, y) -> new entity id (host decides),
  HerdRelation(a, b), FrameNum, Screenshake. Keep units Noita's (pixels, px/s); the game converts.
- `AttachExtra(int shotEntity, string xmlFile)`: load the file as a child of the shot (Noita: extra_entities).
- `Update(frame)`: every LuaComponent runs by Noita's rules: `script_source_file`, `execute_on_added` (runs once at
  once), `execute_every_n_frame` (default 1; -1 = only on added), `execute_times` (-1 = forever),
  `remove_after_executed`, `_enabled`. When an entity dies its children die. Also call scripts on host events if a
  LuaComponent has `script_collision_trigger_hit` / `script_death` etc. (keep a generic `Fire(entity, "script_death")`).
- One MoonSharp Script for all shot scripts; each script file compiled once and run as a chunk per execution (Noita
  runs the whole file each time; it keeps state in components / VariableStorageComponent, not globals). Before each
  run set what `GetUpdatedEntityID()` / `GetUpdatedComponentID()` return. Run inside `LuaCulture.Enter()` and
  `LuaCulture.Prelude` (see LuaGun / LuaWandMaker). `dofile`/`dofile_once` through `read` (data/scripts/lib/
  utilities.lua is Noita's; in tests use a stub). Unknown capitalised API names: a stub metatable that records the
  name in a `Missing` list (copy LuaWandMaker.Stubs) — never throw on them. A script error: logged once per file,
  that component disabled, never kills the game.
- API to implement (count = how many of the 63 scripts call it): GetUpdatedEntityID 63, EntityGetTransform 59
  (x, y, rot, sx, sy), EntityGetComponent 32 (table of ids or nil; optional tag), ComponentGetValue2 21 (typed:
  number/bool/string; for Vector2 fields two numbers), SetRandomSeed 20, ComponentGetValueVector2 20,
  EntityGetRootEntity 18, Random 17 (Noita semantics: Random() float 0..1, Random(a) 0..a int, Random(a, b) int),
  EntityGetFirstComponent 16 (id or nil; optional tag; skips disabled), GameGetFrameNum 16,
  ComponentSetValueVector2 13, EntityGetParent 13 (0 if none), EntityAddTag 10, ComponentGetValue 9 (always a
  string, "" if unset), GetUpdatedComponentID 9, ComponentSetValue2 8, EntityAddComponent 6 (and EntityAddComponent2;
  table of fields -> new comp id), EntitySetTransform 5, ComponentSetValue 5, EntityHasTag 5,
  EntitySetComponentIsEnabled 4, EntityGetInRadiusWithTag 4, EntityGetFirstHitboxCenter 3, ComponentObjectSetValue 3,
  ComponentObjectSetValue2 3, RaytraceSurfacesAndLiquiform 3, EntityLoad 3, EntityAddChild 3, EntityKill 3,
  ComponentObjectGetValue2 2, GameGetVelocityCompVelocity 2, EntityLoadToEntity 2 (adds the file's components to an
  entity), EntityGetHerdRelation 2, EntitySetComponentsWithTagEnabled 2, EntityApplyTransform 2 (= SetTransform),
  ComponentObjectGetValue 2, GameGetGameEffect 1 (comp id or 0), EntityRemoveComponent 1,
  EntityGetFirstComponentIncludingDisabled 1, CellFactory_GetType 1 (a stable int per material name),
  EntityGetAllChildren 1 (table or nil), GameScreenshake 1. Plus the usual ComponentGetIsEnabled, EntityGetName,
  EntityGetComponentIncludingDisabled, EntityGetWithTag, GameGetCameraPos (host), print (no-op).
- Non-Lua components (HomingComponent, SineWaveComponent, ArcComponent, HitEffectComponent, ParticleEmitter,
  MagicConvertMaterial, Lifetime, VariableStorage...) are just stored; the game reads them with a query like
  `Components(entity, type)` including children. LifetimeComponent.lifetime: kill the entity when it runs out.

### 3. Tests and tncli
- tests/Terranoita.Core.Tests: fake host + small hand-written Lua scripts in the test (a sine wave that reads
  mVelocity and writes it back, a VariableStorage counter, EntityAddComponent, execute_every_n_frame/execute_times,
  Base chain merge, a script error disabling only its component, missing API recorded).
- tncli `shot-script <noitaDir> <extra_entity.xml> [frames]`: a fake shot moving right at 300 px/s, the file attached,
  run N frames, print position/velocity every 10 frames and `Missing` APIs at the end. The PC session runs it over
  all 125 files.

### Rules
- Touch only: the two new Core files, tests, src/Terranoita.Cli/Program.cs (new case), MODLOG.md (a new section at
  the end). Do NOT touch src/Terranoita (the game) — the PC session integrates in parallel. Core must build for
  netstandard2.0 and net48 (as now). `dotnet test tests/Terranoita.Core.Tests` green,
  `python3 tools/preflight.py --gate 1a -q` CLEAN.
- Commit and push to the same branch (pull --rebase first). Short English comments in code, like the rest.
- Can be big: do it fully, no stubs left where the semantics above are clear.
