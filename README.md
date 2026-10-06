# Terranoita

**Terraria целиком, как она есть, плюс мир Noita внутри неё.** Мэшап для [Melty](https://melty.gg): игрок нажимает «Играть», и Melty запускает *его собственную* Terraria с модом.

Статус: **черновик, в игре ещё не запускался.** Готовы таблицы дизайна, инструменты и каркас кода. Запуск и проверка в живой игре делаются на ПК, где установлены игры (см. «Продолжение на ПК»).

## Что получит игрок (по этапам)

| Этап | Что появляется в Terraria | Статус |
|---|---|---|
| **1a** | 12 первых врагов Noita (хурты, хийси, нульяски, маги, светлячки…) в подходящих биомах Terraria, с их атаками и снарядами | следующий |
| **1b** | Все обычные враги Noita (~170) | — |
| **1c** | Боссы, миньоны, особые существа Noita | — |
| **2** | Физика Noita на уровне блоков: потревоженные блоки осыпаются, горят, плавятся, взрываются; новые жидкости и газы | — |
| **3** | 4 слота посохов в интерфейсе, сборка заклинаний как в Noita, заклинания | — |
| **4** | 4 слота бутылок, бутылки и предметы Noita | — |

Одиночная игра. Мультиплеер — позже, отдельным решением.

## Решения, согласованные с автором

- Основа — **ванильная Terraria 1.4.5** (мир, генерация, боссы, предметы — всё её). tModLoader **не** используется: Melty его не ставит сам.
- Физика Noita — **на уровне блоков Terraria**, не попиксельно.
- **Нетронутые пещеры, парящие острова и облака не обрушаются.** Физика включается только у потревоженных блоков (выкопан, взорван, подожжён, убрана опора).
- Порядок работ: **враги → физика → посохи и заклинания → предметы**.
- Картинки, анимации и названия Noita **читаются из Noita игрока** (`data/data.wak`), в релиз не копируются. Поэтому игроку нужны обе игры (обе есть в Steam).

## Как это запускается (один клик)

Черновик рецепта Melty — [`design/melty.recipe.draft.json`](design/melty.recipe.draft.json). Melty проверил его (`validate_recipe`, `one_click_check`): **valid, one click: yes**.

- Режим `installed`: файлы мода кладутся **рядом** с `Terraria.exe` (ни один файл игры не меняется).
- Play запускает `{game}/Terranoita.exe --noita-dir {game:noita}`.
- `Terranoita.exe` загружает `Terraria.exe` в свой процесс, `Terranoita.Game.dll` ставит Harmony-патчи, затем стартует обычная Terraria. Запуск `Terraria.exe` напрямую — чистая Terraria.
- Лог: `%LOCALAPPDATA%/Terranoita/logs/latest.log`.

Корневой `melty.json` появится только после согласия автора.

## Как устроена работа: таблицы — источник правды

Всё, что мод добавляет или трогает, описано в JSON-таблицах [`design/sheets/`](design/sheets): строка — одна вещь, столбец — её свойство.

| Таблица | Строк | Что |
|---|---|---|
| `enemies` | 203 | Каждое существо Noita: здоровье, атаки, архетип ИИ, где появляется, уровень, уязвимости |
| `attacks` | 309 | Каждая атака каждого врага |
| `projectiles` | 120 | Снаряды врагов (на этапе 3 станут заклинаниями) |
| `ai_archetypes` | 37 | Архетипы поведения (ходок-стрелок, левитирующий маг, червь, призрак…) и боссы |
| `biome_map` | 42 | Локации Noita → зоны Terraria |
| `terraria_zones` | 26 | Зоны Terraria и точная проверка в коде |
| `balance` | 4 | Уровни прогрессии: как числа Noita становятся числами Terraria |
| `drops` | 3 | Добыча |
| `systems` | 14 | Каждая система игры, которую трогает мод |
| `hooks` | 15 | Каждый патч в Terraria |

Каждая клетка — галочка. Пустая (`null`) или помеченная в `_unverified` клетка — незаконченная строка.

```sh
python tools/preflight.py              # все незаполненные/непроверенные клетки и битые ссылки
python tools/preflight.py --gate 1a    # можно ли собирать этап 1a (сейчас: нет, 172 пункта — все требуют игр)
python tools/gen_cs.py --gate 1a       # C#-таблицы из листов (только при чистом preflight)
python tools/gen_cs.py --draft         # черновая генерация для проверки компиляции
```

Сначала меняется таблица, потом код. Сгенерированный код (`*.g.cs`) руками не правится.

Данные врагов засеяны из [Noita Wiki](https://noita.wiki.gg/wiki/Enemy_Information_Table) (`tools/seed_sheets.py`, разово), а пути к файлам, хитбоксы, тайминги и снаряды заполняются из `data.wak` игрока инструментами ниже.

## Код

| Путь | Что | Собирается здесь? |
|---|---|---|
| `src/Terranoita.Core` | Чтение `data.wak`, XML сущностей и спрайтов Noita, переводов; сгенерированные таблицы (netstandard2.0) | да, 8 тестов проходят |
| `src/Terranoita.Cli` (`tncli`) | Инструмент разработчика: читает `data.wak` и выдаёт факты для таблиц | да |
| `src/Terranoita.Launcher` | `Terranoita.exe` (net48, x86): загрузка Terraria и мода | да |
| `src/Terranoita` | `Terranoita.Game.dll`: Harmony-патчи; компилируется против `Terraria.exe` игрока | только на ПК с Terraria |
| `tests/Terranoita.Core.Tests` | Тесты Core (синтетический `data.wak`, XML, спрайты, переводы, таблицы) | да |

```sh
dotnet test tests/Terranoita.Core.Tests
```

## Продолжение на ПК (для агента на компьютере автора)

Ответы автора выше — не переспрашивать. Порядок:

1. Установить .NET 8 SDK (официальный установщик Microsoft) и `ilspycmd` (`dotnet tool install -g ilspycmd`). Python 3 — для `tools/`.
2. Найти папки игр в библиотеке Steam (`steamapps/common/Terraria`, `steamapps/common/Noita`; другие библиотеки — в `steamapps/libraryfolders.vdf`). Записать в `MODLOG.md`.
3. Факты из Noita → таблицы:
   ```sh
   dotnet run --project src/Terranoita.Cli -- wak-list "<Noita>" enemies_gfx/zombie   # убедиться, что формат data.wak читается
   dotnet run --project src/Terranoita.Cli -- facts "<Noita>" design/sheets/enemies.json build/noita_facts.json
   python tools/apply_facts.py build/noita_facts.json --stage 1a
   python tools/preflight.py --gate 1a
   ```
   Что `apply_facts` не сопоставил — разобрать вручную через `tncli entity` / `wak-cat`, и вписать в таблицу.
4. Декомпилировать `Terraria.exe` (1.4.5.x) **вне репозитория** (`ilspycmd -p -o %USERPROFILE%\terraria-decomp Terraria.exe`). По нему проверить и исправить таблицу `hooks` (точные сигнатуры), `systems` (x86/XNA, точка входа, Steam), выбрать «носителя» NPC и снаряда (тип с минимумом особых случаев), заполнить `terraria_zones.terraria_check`.
5. Сделать `preflight --gate 1a` чистым (баланс и скорости ИИ — плейтест), затем `gen_cs.py --gate 1a`.
6. Написать патчи `src/Terranoita` по таблицам `hooks`/`systems` (каждый патч-класс с `[Hook("<id>")]`; `Entry.Start` пишет в лог, каких не хватает), ИИ-архетипы этапа 1a — в `Terranoita.Core/Ai` с тестами без игры.
7. Собрать, положить в папку Terraria, запустить `Terranoita.exe --noita-dir "<Noita>"`, проверить по логу и скриншоту, что враги Noita появляются, ходят, атакуют, умирают, дают монеты. Снять скриншот/клип игрового окна.
8. Melty: упаковать zip, `inspect_package` → `validate_recipe` → `one_click_check`, `create_mod` (githubRepo `tsartsinc-web/terranoita`), загрузка, `submit_release`, медиа. Показать автору итог, попросить нажать **Test** в приложении Melty, публиковать только с его разрешения. Лицензию и разрешение на ремиксы спросить у автора (ещё не выбраны).

## Авторы и источники

- Terraria © Re-Logic, Noita © Nolla Games. Файлы игр в репозитории и релизе **не содержатся**: мод читает их с ПК игрока.
- Таблица существ Noita — [Noita Wiki](https://noita.wiki.gg) (CC BY-SA 4.0), `design/sources/noita_wiki_creatures.json`.
- [Harmony](https://github.com/pardeike/Harmony) (MIT) — патчи во время работы.
- Сделано с помощью ИИ (Claude Code) по идее и решениям автора.

Лицензия проекта: **ещё не выбрана**.
