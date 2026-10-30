# Dialoid

Диаблоид (ARPG в духе Diablo/PoE2/Last Epoch). Сейчас в репозитории `Dialoid.Core` — чистая C#-библиотека правил игры без Godot, — контент в `data/`, `Dialoid.Sandbox` (сессия редактирования билда) и отладочная сцена на Godot в `game/`.

## Документы

- Спецификация: [docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md](docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md)
- План 1 (движок правил): [docs/superpowers/plans/2026-10-25-dialoid-core-engine.md](docs/superpowers/plans/2026-10-25-dialoid-core-engine.md)
- Заметки движка и закрытые хвосты планов: [docs/superpowers/plans/2026-10-25-dialoid-core-engine-notes.md](docs/superpowers/plans/2026-10-25-dialoid-core-engine-notes.md)
- Скриншоты отладочной сцены: [docs/superpowers/debug-scene-screenshots/](docs/superpowers/debug-scene-screenshots/)
- Справочник автора данных: [docs/data-authoring-reference.md](docs/data-authoring-reference.md)
- Вопросы владельцу по контенту и балансу: [docs/superpowers/content-owner-questions.md](docs/superpowers/content-owner-questions.md)
- Журнал баланса: [data/BALANCE_NOTES.md](data/BALANCE_NOTES.md)

## Ядро: как запустить тесты

```bash
dotnet test core/Dialoid.sln
```

Требуется .NET SDK 8 или новее (цель сборки — `net8.0`, на новых рантаймах включён `RollForward=Major`). Предупреждения компилятора считаются ошибками.

## Архитектура ядра

`Dialoid.Core` — одна сборка; пространства имён ниже — это модули, а не строгие слои: часть из них ссылается друг на друга.

- **Stats** — фундамент, ни от чего не зависит: статы, модификаторы Flat/Increased/More/Override, формула, граф зависимостей `per`, условия.
- **Effects** — эффекты узлов, области действия, интерфейс механик `IMechanic` (параметры, допустимые области, хуки) и реестр. Реестр по умолчанию регистрирует каталог из **Mechanics**, а контексты хуков используют типы **Simulation** — эти модули связаны в обе стороны.
- **Character / Passives / Skills** — билд и его правила (`BuildRules`), компиляция персонажа, дерево пассивок, деревья и компиляция скиллов.
- **Data** — загрузка JSON из `data/` и валидатор с русскими сообщениями (файл, id, причина). Определения контента живут в модулях, которые их используют, поэтому Data зависит от типов Character, Combat, Effects, Passives, Skills и Simulation (например, `DummyDefinition`), а они — от `GameData`.
- **Combat** — базовый урон, конверсия, митигация, попадание, недуги. Combat и Simulation делят `AilmentInstance` (тип лежит в Simulation, Combat его создаёт).
- **Mechanics** — механики классов и ключевых узлов (C#, подключаются по id из данных).
- **Simulation** — детерминированная симуляция боя («среднее» и случайный режим, защитный режим с атакующим манекеном).
- **Builds** — экспорт и импорт билда: код `DLD1:` + base64url(gzip(JSON)), JSON в snake_case.

Загрузка данных проверяет всё, что движок читает по имени: обязательные статы (`RequiredStats`), каталог тегов (плюс динамический `skill:<id>`), ссылки на скиллы, баффы и узлы, параметры поведения (`BehaviorKeys`), параметры и области механик. Новый скилл или узел, собранный из существующих эффектов, условий и механик, добавляется только данными, без правок C#.

### Дополнения движка к спецификации

Записаны здесь, как просит спецификация:

- условие `target_frozen` (цель под заморозкой; нужно «Расколу»);
- механика `blood_feast` — «Кровавый пир»: +1% скорости атаки за стак кровотечения на основной цели, максимум 15% (стат-часть — обычный модификатор `bleed_max_stacks`);
- механика `ground_type` — тип урона наземной зоны скилла («грозовая земля» «Разряда» — молния даже на физическом скилле);
- механика `chill_slow` — сила охлаждения вместо стандартных 15% (у «Вечной мерзлоты» 25%);
- песочное правило `spread_ignite`: в паке поджог, наложенный использованием, через `delay` (1 с) считается наложенным на все живые цели пака, которых он не достал (позиций в песочнице нет, поэтому «передача соседям при смерти» не моделируется);
- песочное правило `fire_armor`: если в слотах есть скилл, создающий горящую землю (`ground_duration` и `ground_fraction` > 0, огненная зона), персонаж получает +30% брони (Increased) и +10 к максимуму сопротивления огню.

## Контент

Что есть в `data/`:

- два класса (Воитель, Чародей), по шесть основных скиллов с деревьями развилок (плюс производные мини-скиллы: Возвращающийся топор, Огненный взрыв, Вспышка телепорта — `returning_axe`, `fireball_blast`, `blink_flare`), один скилл из лута (Ядовитый туман, `poison_cloud`);
- пассивное дерево и 6 keystones, четыре асцендента (Берсерк, Громовержец, Пироман, Криомант) по 6 notables;
- атрибуты, оружие, предметы, манекены («Тренировочный», «Рой», «Босс», «Атакующий босс»);
- эталонные билды в `data/builds/` (шесть основных 60 уровня, версии 20 уровня, паритет, пары для keystones; сводка с метриками — `data/builds/README.md`).

Как добавить контент: всё, что собирается из существующих эффектов, условий и механик, добавляется только JSON-файлами в `data/`, без правок C#. Формат каждого каталога, список стат, тегов и механик — в [справочнике автора данных](docs/data-authoring-reference.md). Загрузчик строгий: неизвестное поле или ссылка на несуществующий id — ошибка загрузки на русском (файл, id, причина). Каждое отклонение от числа спеки записывается в `data/BALANCE_NOTES.md`.

Тесты контента и баланса (`core/Dialoid.Core.Tests/Content/`):

```bash
# все тесты, включая приемку баланса (режим Average)
dotnet test core/Dialoid.sln
# только контент и баланс
dotnet test core/Dialoid.Core.Tests --filter "FullyQualifiedName~Dialoid.Core.Tests.Content"
# все метрики приемки одной таблицей
dotnet build tools/BuildOptimizer -c Release
dotnet tools/BuildOptimizer/bin/Release/net8.0/BuildOptimizer.dll suite --all
```

Пересборка эталонных билдов после изменения чисел: `tools/BuildOptimizer/rebuild_references.sh` (инструмент разработчика вне решения; запускать только последовательно, полная пересборка около 1,5 часа). После нее проверить diff в `data/builds/`, прогнать тесты и обновить метрики в `data/builds/README.md` (таблицы собраны из вывода `suite --all`).

## Отладочная сцена

Godot-проект `game/` — отладочная сцена (спека §13): тонкий слой отрисовки над `Dialoid.Sandbox`, правил игры в нём нет. Интерфейс строится кодом в `Main._Ready()`, вкладки: «Комната» (3D-комната тренировки, план 4), «Персонаж», «Пассивки», «Асцендант», «Скиллы», «Симуляция», «Разбор», «Билды». Рендерер Forward+, физика 3D — Jolt Physics.

Разработка и приемка идут на Windows (PowerShell 5.1). Нужен Godot 4.7 .NET, консольная сборка `Godot_v4.7.2-stable_mono_win64_console.exe` (версия `Godot.NET.Sdk` в `game/Dialoid.Game.csproj` совпадает с редактором), и .NET SDK (`dotnet`). Где ищется Godot: переменная `GODOT`, иначе `C:\Users\sibiya666\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`, иначе `godot` из PATH. Другой путь:

```powershell
$env:GODOT = 'D:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe'
```

Запуск: открыть `game/project.godot` в редакторе Godot и нажать «Запустить» (F5) — или из корня репозитория:

```powershell
dotnet build game/Dialoid.Game.csproj
& 'C:\Users\sibiya666\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --path game
```

Данные сцена ищет сама: ближайший `data/` с `classes.json` вверх от `game/`. Другой каталог данных задаётся переменной `DIALOID_DATA`. Если данные не найдены или не загрузились, сцена показывает ошибку и проверенные пути, а в лог пишет строку `DIALOID_LOAD_FAILED: <причина>`.

Проверка сборки без окна (`dotnet build` 600 с → `tools/sync_assets.ps1` → `godot --headless --import` 600 с → `godot --headless --quit` 60 с; каждый шаг под лимитом, при превышении процесс и его дочерние убиваются):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot_check.ps1
# проверка сцены без данных: ошибка загрузки обязательна, ERROR в логе — нет
$env:DIALOID_DATA = 'C:\nonexistent'
powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot_check.ps1 -ExpectLoadFailure
Remove-Item Env:DIALOID_DATA
# первый импорт десятков FBX долгий: лимит импорта в секундах
powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot_check.ps1 -ImportTimeoutSec 1200
```

### Ассеты комнаты

Модели, анимации и текстуры в git не кладутся (лицензии). Сцена ищет их в папке из переменной `DIALOID_ASSETS`, иначе в `assets/` в корне репозитория (она в `.gitignore`). Таблица «id → файл» — `game/data/assets.json` (имена файлов как на диске). Перед импортом `tools/godot_check.ps1` вызывает `tools/sync_assets.ps1`: он копирует в `game/vendor/` (вне git) **только файлы из таблицы**, если их там нет или исходник новее, с нормализованными именами (нижний регистр, пробелы и скобки → `_`: `sword and shield slash (2).fbx` → `sword_and_shield_slash_2.fbx`), печатает отсутствующие и предупреждает, если нет учета лицензий `LICENSES.md`. Файл, исчезнувший из ассетов, убирается и из `game/vendor/`. Вручную:

```powershell
$env:DIALOID_ASSETS = 'D:\dialoid-assets'   # не обязательно: по умолчанию assets/ в корне
powershell -NoProfile -ExecutionPolicy Bypass -File tools/sync_assets.ps1
```

Без ассетов комната рисуется заглушками (капсулы того же роста, однотонные материалы), самопроверка проходит. На каждый отсутствующий файл в логе одна строка `ASSET_MISSING: …` (не `ERROR`). Настройки импорта FBX не хранятся в `.import` (папка вне git): имя клипа берется из имени файла (в Mixamo все клипы называются `mixamo.com`), зацикливание стойки, ходьбы и бега, прибитый корень (у бедер обнуляются X и Z), перепись путей костей под скелет модели (`mixamorig:`, `mixamorig1:`, … ) и масштаб по измеренной высоте модели делаются в коде (`game/Room/AssetLibrary.cs`). Текстурам скрипт пишет `.import` со сжатием VRAM и мипмапами.

Что скачать и куда положить:

| Что | Откуда, настройки | Куда |
|---|---|---|
| Персонажи `Paladin WProp J Nordstrom` (герой), `Skeletonzombie T Avelange` (мертвец), `Warrok W Kurniawan` (орк и босс) | mixamo.com, Download: Format **FBX Binary**, Pose **T-pose**, Skin **With Skin** | `assets/mixamo/characters/` |
| Пакет `Sword And Shield Pack` (стойка, ходьба, бег, удары, каст, реакция на удар) | mixamo.com, Download: **FBX Binary**, Skin **Without Skin**, **30 fps**; распаковать | `assets/mixamo/anims/sword/` |
| Пакет `Scary Zombie Pack` (стойка, ходьба, атака мертвеца) | так же, **Without Skin**, **30 fps** | `assets/mixamo/anims/zombie/` |
| Текстуры `monastery_stone_floor` (пол), `castle_wall_slates` (стены), `medieval_blocks_02` (колонны, пилястры), `medieval_wood` (бочки) | polyhaven.com, CC0, **2K JPG**: `diff`, `nor_gl`, `rough` | `assets/polyhaven/<имя>/diff.jpg`, `nor_gl.jpg`, `rough.jpg` |

Учет лицензий — `assets/LICENSES.md` (Mixamo: условия и ссылка; Poly Haven: CC0, страницы текстур; кровь — процедурная). Без него сборки с ассетами не публикуются.

Качество графики: по умолчанию высокое; `-- --quality low` (минимальные требования: GTX 1050 Ti / RX 560, 1080p ≥ 30 кадров/с) выключает SSAO, тени факелов, объемный туман и свет снарядов — остаются тень героя и контур.

### Самопроверка

Самопроверка сцены (`-- --selftest`, лимит 300 с) нажимает настоящие панели теми же обработчиками, что и мышь, печатает по строке на шаг (`SELFTEST OK …` / `SELFTEST FAIL …: причина`) и выходит с кодом 0, если все шаги прошли, иначе 1:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot_check.ps1 -SelfTest
# то же вручную, из корня репозитория
& 'C:\Users\sibiya666\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path game -- --selftest
```

Приемка (спека §15.4) входит в самопроверку. Для каждого из шести основных эталонов билд загружается во вкладке «Билды» (история «Разбора» при этом очищается), «Тренировочный» манекен считается в режиме «среднее» во вкладке «Симуляция», и DPS панели сверяется с `Simulator.Run` по тому же файлу (до бита). Затем меняются два узла главного скилла (скилл с наибольшим DPS):

- **узел дерева скилла:** кнопкой «−» во вкладке «Скиллы» снимается очко с узла, названного в разборе множителя урона попадания (`hit_mult_*`): DPS меняется, строка узла в этом разборе меняется или пропадает, «+» возвращает DPS точно. Если «−» у всех таких узлов отклонен (на них держатся зависимые узлы, так у огненного Рассекающего удара), берется узел из разбора крита или пробивания, и таблица называет отклоненные; если подходящего узла нет совсем, шаг падает;
- **notable пассивного дерева:** кликом по дереву снимается взятый notable, названный в разборе главного скилла: DPS меняется, в «Разборе» пропадает строка узла, после возврата DPS прежний. Если у билда нет notable, снятие которого не рвет путь к старту (так у «Чародей, Криомант — Ледяное копье»), в таблице стоит «—», другой узел не подставляется.

Итог — таблица `SELFTEST TABLE билд | главный скилл | DPS до | узел скилла «−» | notable`. Еще самопроверка проверяет, что подписи пассивного дерева не перекрывают друг друга и узлы (подпись, которой некуда встать, видна при наведении).

Скриншоты всех вкладок (эталон пиромана после симуляции, плюс «Разбор» множителя урона попадания) снимаются в окне, не в `--headless` (самопроверка в окне, лимит 300 с):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot_check.ps1 -Screenshots docs/superpowers/debug-scene-screenshots
```

Коды выхода `tools/godot_check.ps1`: 0 — всё в порядке; 1 — не прошла сборка или импорт, в логе Godot есть строка `ERROR:`/`SCRIPT ERROR:` (после снятия ANSI-кодов), или с `-ExpectLoadFailure` данные загрузились; 2 — Godot не найден или неверный аргумент; 3 — сцена не загрузила данные игры (строка `DIALOID_LOAD_FAILED:`); с `-SelfTest`/`-Screenshots` — код самопроверки (он важнее проверок лога); 124 — шаг не уложился в лимит времени.

### Mac

На Mac та же проверка — `tools/godot_check.sh` (bash; Godot: `GODOT`, иначе `/Applications/Godot_mono.app/Contents/MacOS/Godot`, иначе `godot` из PATH; установка `brew install --cask godot-mono`):

```bash
tools/godot_check.sh
tools/godot_check.sh --selftest
DIALOID_DATA=/nonexistent tools/godot_check.sh --expect-load-failure
```

Коды выхода те же, лимит импорта 120 с, при превышении лимита код 124 или 142 (perl alarm).

`game/` не входит в `core/Dialoid.sln`: проект на Godot.NET.Sdk собирает этот скрипт.

## Статус

| План | Состояние |
|------|-----------|
| План 1: движок правил Dialoid.Core | выполнен |
| План 2: контент и баланс | выполнен (открытые вопросы — `docs/superpowers/content-owner-questions.md`) |
| План 3: отладочная сцена Godot | выполнен (самопроверка и приемка §15.3–15.4, скриншоты в `docs/superpowers/debug-scene-screenshots/`) |

Этап 1 (правила, контент и баланс, отладочная сцена) завершен. Дальше — визуал отдельным этапом: 2D-пиксель-арт. Решения владельца по контенту, балансу и визуалу собираются в [docs/superpowers/content-owner-questions.md](docs/superpowers/content-owner-questions.md).
