Terranoita, ветка `claude/dazzling-carson-h8mi9n` (сначала `git pull`). Прочитай CLAUDE.md, design/progress_window.md.
Только Core и тесты (игру правит локальная сессия). Две задачи.

## 1. LuaShotScripts: быстрее (игра шла 30 FPS при ~20 снарядах со скриптами)
- `Update(frame)` каждый кадр перебирает все `_comps` через LINQ (`Where(...).ToList()`, `OrderBy`) и Sweep.
  Держи отдельные списки: LuaComponent'ы по порядку id и LifetimeComponent'ы (добавлять/убирать при Add/Remove/Kill/Forget),
  без аллокаций в горячем пути; `NextFrame` — пропуск без вызова Lua. Sweep — только если кто-то умер в этом кадре.
- `EntityGetWithTag` / `EntityGetInRadiusWithTag` / `EntityGetClosestWithTag`: индекс тег -> множество сущностей
  (обновлять в AddTags/EntityAddTag/EntityRemoveTag/Kill).
- Скомпилированный chunk на файл уже есть; проверь, что `RunSource` не создаёт лишних DynValue/таблиц на вызов.
- Тест-бенч (xunit, без порогов по времени, только счётчики): 300 снарядов x 2 скрипта x 600 кадров — проверить, что
  число выполнений скриптов и результаты те же, что до изменений; плюс простой Stopwatch-вывод в лог теста.

## 2. Core/Progress/ProgressInfo.cs — строки подсказок окна прогресса из сгенерированных таблиц
- creature (Enemies id): где живёт (BiomeMap / TerrariaZones — какие биомы Террарии), атаки (Attacks: имя/тип, урон
  в hp Террарии = урон Ноиты * 25), жизнь в Террарии (как считает игра: NoitaHp * 25 * множитель тира, если он есть в Core).
- liquid (Liquids id): что делает (StatusEffects / Reactions: статус при касании, урон, горит ли, с чем реагирует).
- spell (SpellTable id): тип, мана, заряды; урон и взрыв из SpellProjectiles его снарядов, если связь есть в таблицах.
- API: `static List<string> Lines(string category, string id)` (категории как в ProgressBook), без имени (имя даёт игра,
  переводы из Ноиты). Без Terraria-типов. Тесты на 2-3 известных id каждой категории.

## Правила
- Не трогай src/Terranoita (игру). Core собирается под netstandard2.0 и net48. `dotnet test tests/Terranoita.Core.Tests`
  зелёный, `python3 tools/preflight.py --gate 1a -q` CLEAN.
- MODLOG: новый раздел в конце; commit + push (pull --rebase перед push).
