Terranoita, ветка `claude/dazzling-carson-h8mi9n` (сначала `git pull`). Прочитай CLAUDE.md и design/review_physics_2026-10-08.md
(твоё же ревью). Задача: исправить пункты 1-5 этого ревью в коде игры. Игр здесь нет: src/Terranoita не собирается в облаке,
поэтому пиши аккуратно (C# 7.3, net48, как соседний код); сборку и тест в игре сделает локальная сессия.

## Что сделать
1. Fluids.Save/Load, Placed.Save, ToxicGround.Write: снимок данных на главном потоке (или общий lock с Update), запись в
   `path + ".tmp"` и замена старого файла (File.Replace, если старого нет — File.Move); в Load PoolsVersion только после
   полного чтения; при битом файле — переименовать его в `.bad`, пулы НЕ генерировать заново. Terraria 1.4.5 автосохраняет мир
   в ThreadPool (WorldGen.saveAndPlay -> WorldFile.SaveWorld) — считай, что Save может идти не с главного потока.
2. Falling: набор `checkedThisFrame` — тайлы групп, уже проверенных в этом кадре, CheckSupport их пропускает.
3. Placed-блоки, упавшие на занятый тайл или ниже мира: выпадают предметом (Item.NewItem с типом предмета тайла;
   если тип не найти — как сейчас). Найди, как код уже получает item type тайла, или оставь TODO с пометкой «проверить на ПК».
4. Fire.HurtNpcs: хитбоксы NPC один раз за тик (active, !friendly, !dontTakeDamage), потом проверка горящих тайлов против них.
5. PlaceTilePatch.Postfix в try/catch (Entry.Error); Land: вода Террарии на месте посадки не удаляется, а сдвигается вверх
   (или оставь как есть с комментарием, если сдвиг сложен).
6. Заодно из design/review_magic_2026-10-08.md пункты 8 и 9 (Fluids.ConvertMaterial: если `to` не известная жидкость и не
   water/lava/"air" — ничего не трогать; у Ignite вернуть свой `<summary>`, у ConvertMaterial — свой).

## Правила
- Трогай только src/Terranoita/Physics/*, src/Terranoita/ToxicGround*.cs (где он есть), MODLOG.md (новый раздел в конце).
  Магию (src/Terranoita/Magic/*) не трогай — её сейчас правит локальная сессия.
- Если нужна информация из Терраии/Ноиты (сигнатуры, файлы) — запиши вопрос в MODLOG «вопрос локальной сессии», не гадай.
- Коммит и push в ту же ветку (сначала pull --rebase). В MODLOG: что сделано и «проверить на ПК: game_test -Mode physics».
