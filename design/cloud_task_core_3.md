Terranoita, ветка `claude/dazzling-carson-h8mi9n` (сначала `git pull`). Прочитай CLAUDE.md. Только Core + тесты.

## Задача: снаряд из любого файла сущности Ноиты во время игры
Скрипты заклинаний делают EntityLoad файлов, которых нет в design/sheets/spell_projectiles.json (лог игры:
"spell EntityLoad not done yet: data/entities/misc/orbit_discs_disc.xml", deck/wall_builder.xml, wall_piece.xml,
wall_sound.xml, chain_bolt_explosion.xml; misc/quantum_split.xml ...). Игра уже умеет делать выстрел по
`SpellProjectileDef` (Generated/SpellProjectiles.g.cs). Нужно делать такой Def на лету из файла.

1. `Core/Noita/SpellProjectileFromEntity.cs`: `static SpellProjectileDef From(XmlEntity e)` (NoitaEntityXml.Load даёт
   Base-цепочку, поля компонентов и вложенные "config_explosion.x"). Те же правила, что tools/apply_spells.py
   (rows "spell_projectiles", SHOT_COLUMNS): ProjectileComponent (speed_min/max, lifetime, lifetime_randomness, damage,
   damage_by_type.*, on_death_explode, explosion_dont_damage_shooter, config_explosion.explosion_radius/damage,
   bounces_left, penetrate_entities, die_on_low_velocity, collide_with_world, on_collision_die, damage_every_x_frames...),
   VelocityComponent (gravity_y, air_friction), SpriteComponent image_file -> Sprite, CellEater/AreaDamage/
   Audio, material (ParticleEmitter / MaterialInventory как в apply_spells.py). Перенеси правила 1:1 из Python
   (названия колонок, единицы, значения по умолчанию из component_documentation, если Python так делает).
   Нет ProjectileComponent -> null.
2. Тест-сверка: для 3-5 строк из spell_projectiles.json собери XML по их полям (синтетические файлы в тесте) и проверь,
   что From даёт те же числа, что строка листа. Плюс файл без ProjectileComponent -> null, Base-цепочка.
3. В MODLOG: что Def, сделанный так, совпадает с листом по правилам; локальная сессия подключит его в
   SpellShots.Def(file) как запасной путь (кэш по файлу) и прогонит тест магии.

## Правила
- Не трогай src/Terranoita (игру) и листы. Core: netstandard2.0 + net48. Тесты зелёные, preflight 1a CLEAN.
- commit + push (pull --rebase перед push).
