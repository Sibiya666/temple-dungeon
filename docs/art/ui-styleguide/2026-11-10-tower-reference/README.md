# Башня: эталон общей дизайн-системы

Beads: dialoid-2tt. Изолированный target: `C:/Users/sibiya666/.codex/worktrees/tower-ui-reference/dialoid`. Источник: `tower-activities-ku0`, HEAD `4fc2734c31805fe22ea4f09117d8b1f7c6833e1c` с существующим WIP. Основной checkout не является базой этого UI patch.

## Исходное состояние и границы

В target скопированы 18 изменённых tracked файлов и относящиеся к Башне untracked UI/ассеты/происхождение; всего103 файла, SHA256 проверены (`snapshot-manifest.json`). `baseline-files/` и `baseline-manifest.json` сохраняют точное исходное состояние шести изменяемых файлов. `source-status-before.txt` фиксирует состояние source до работы. Существующие Core/data изменения принадлежат исходной задаче Башни и НЕ входят в UI-only patch.

`game/vendor/{dialoid-room-v1,mixamo}` скопирован из существующего source только для настоящей runtime-проверки; .env/.godot/секреты не копировались. Импорт выполнен в target. Первичный запуск без vendor был диагностическим; финальная сцена имеет0 missing assets.

## Изменение оформления

Одна внешняя холодная металлическая оправа, внутренние секции разделены отступами, спокойные тёмные подложки. Из карточек/кнопок/ячеек убраны вложенные декоративные контуры. `RoomHudTheme` содержит opt-in именованные параметры текста/интервалов и независимые `ReferenceSurface`/`ReferenceFocus`. Другие окна не переключены на новую тему.

В правой панели выбранная активность обозначена существующим40px портретом, заголовком20px, контекстом14px. Настоящие rule-derived details, количество, свойства, операция и цель сохранены. Пустой этаж/вся Башня имеют собственное текстовое обозначение. Прогнозы наград не выдаются за уже полученную награду.

Срезанные симметричные гнёзда4×4 и их hit targets сохранены. PNG сиреневой/бледно-жёлтой/холодно-белой палитры не изменялись. В `TowerGlassWindow` COLOR является кодированием материала: r около.25 означает контур, g задаёт интенсивность, b>.5 — активный контур; shader decode теперь даёт холодный металл вместо предложенного золота. Выбор замкнут; hover меняет грань; фокус добавляет противоположные угловые метки, не меняя рисунок/геометрию/счётчик. Фреймы отделены от иллюстраций. Новых концептов, генераций, покупок и механик нет.

## Доказательства

- `before/reference-{720,900}-{empty,selected-focus,gamepad,installed,world-hud}.png` — исходная настоящая доска и мир/HUD.
- `after/` — те же десять финальных кадров.
- `tower-before-after.png` — две настоящие720p доски1:1 рядом, без перерисовки UI; воспроизводится `& ./build-comparison.ps1`.
- `tower-ui-only.patch` — только9 собственных UI/test файлов относительно snapshot; `patch-base/` и `patch-final/` — проверяемые текстовые стороны этого diff. В patch нет Core/data/source WIP/vendor/provenance документов.

## Проверки и команды

Из target, PowerShell:

```powershell
dotnet restore game/Dialoid.Game.csproj --ignore-failed-sources -p:NuGetAudit=false
dotnet build game/Dialoid.Game.csproj --no-restore
$env:APPDATA = Join-Path (Get-Location) 'game/.godot/tower-reference-user'
$env:DIALOID_SELFTEST_FILTER = 'tower.'
& 'C:\Users\sibiya666\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path game -- --selftest
```

Build:0 предупреждений/ошибок. Tower:15/15 PASS, без shutdown warning. Исходные14 проверяют мышь, клавиатуру, геймпад, containment/scroll/focus, операции, расходники и перенос инвентаря. Новый reference проверяет установку/возврат/количество/причину disabled. Это целевая проверка, не полный selftest игры.

```powershell
$env:DIALOID_SELFTEST_FILTER = 'tower.reference'
& 'C:\Users\sibiya666\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --path game -- --selftest --screenshots 'C:/Users/sibiya666/.codex/worktrees/tower-ui-reference/dialoid/docs/art/ui-styleguide/2026-11-10-tower-reference/after'
```

GPU:2/2 PASS, Vulkan AMD RX6750XT, настоящие1280×720/1600×900,0 missing assets. Geometry берётся ДО focus, затем проверены неизменность ноды/портрета/счётчика и помещение счётчика внутри ноды. Финальные PNG просмотрены. Headless reference не выполняет дополнительную визуальную focus/device-последовательность и не ресайзит dummy renderer: семантика выполняется при исходном размере, а визуальные проверки остаются на реальномGPU. Бой в production и тестах не отключён/не изменён.

Интегратор независимо выполнил baseline Core Tower:186/186 PASS:

```powershell
dotnet restore core/Dialoid.Core.Tests/Dialoid.Core.Tests.csproj --ignore-failed-sources -p:NuGetAudit=false
dotnet test core/Dialoid.Core.Tests/Dialoid.Core.Tests.csproj --no-restore --filter FullyQualifiedName~Tower
```

`git diff --check` и `git apply --check --whitespace=error <tower-ui-only.patch>` к source проходят;103 source SHA256 повторно совпали перед передачей. Patch ещё не применялся исполнителем. Результат локальный, не commit/main delivery.

## Проверенное ограничение headless

Промежуточный новый reference с дополнительным focused presentation после старых14 давал при exit `1 RID DummyTexture / Leaked Image`. Изолированные open-close и reference были чистыми; target14 без регистрации reference тоже чистый. Пауза боя и отказ от headless resize по отдельности warning не устраняли. После переноса новой визуальной focus/device-последовательности только вGPU-проход итог15/15 чистый. Логи partition: `tower-ui-without-reference.log`, `reference-headless-verbose.log`, `diagnostic-early-return.log`, `diagnostic-before-install.log`; финальные `tower-final.log` и `reference-final.log`. Это локализация синтетической тестовой последовательности, не утверждение о причине Godot и не production leak fix.

## Приёмка и перенос

Независимые арт- и UI-кураторы смотрят фактические after кадры и UI-only diff; итоговые verdict фиксирует интегратор в TASK-tower-ui-reference.md. Существующий долг других смысловых иконок/контента не закрывается оформлением. Интеграция этого UI patch в source — отдельное действие интегратора после его проверок; перенос исходного gameplay WIP в main не входит в работу.
## Интеграционная проверка основного агента

Проверенный UI-only patch применён основным агентом в актуальную tower-activities-ku0 после сверки 109 baseline-записей. Source build: 0 warnings/errors; import: PASS; Tower: 15/15 PASS clean; graphical reference: 2/2 PASS, 720/900. Кадры этой повторной проверки — integrated-after/. Просмотрен integrated-after/reference-720-selected-focus.png. 99 файлов чужого snapshot вне UI-патча остались неизменны, 9 UI-файлов побайтно совпадают с кандидатом; integration-receipt.json сохраняет результат. Арт и UI/code: APPROVE. Это локальная интеграция без коммита и без main delivery; отдельная изолированная копия остаётся для воспроизведения/ревью.
