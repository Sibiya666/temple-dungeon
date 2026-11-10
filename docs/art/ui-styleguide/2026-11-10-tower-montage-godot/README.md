# Башня — монтажный пролёт: Godot evidence

**Текущий визуальный результат:** [исправление соответствия выбранному05](parity-correction/README.md), независимая [root-квитанция](parity-correction/root-receipt.md), кадры `parity-correction/root-after/`. Первый проход ниже отклонён владельцем из-за утраты фона и существенных отличий композиции; его кадры и прежние APPROVE сохранены только как история. После исправления новые арт/UI APPROVE, root build0/0, GPU17/17 и headless17/17.

Локальная реализация в изолированном tower-ui-reference, без коммита и без утверждения о main. Task dialoid-7wx. Новые навыки, узлы, иллюстрации и расходы не добавлялись. Core/data/Main/project/theme/asset manifest не изменены этим исполнителем.

## Изменённые файлы

- game/Ui/Tower/TowerBoardView.cs — композиция/краткие этажи, каталог8колонок, независимый источник, динамические1–2ряда при ёмкости страницы16, wheel hit area.
- game/Ui/Tower/TowerBoardView.ReferenceStyle.cs — постоянный detail источника, отдельный установленный этаж, правдивые причина/свойства/качество.
- game/Ui/Tower/TowerBoardView.Montage.cs — выбранная композиция05 на существующих Godot controls, biome/properties/actions/source/currency target.
- game/Ui/Tower/TowerGlassWindow.cs — общий square SocketBounds для glass/frame/focus; proportional cover/crop и alpha0.25 только decorative fill. Рисунки/шейдеры/контуры выбора не менялись.
- game/SelfTest.Tower.cs — сохранены реальные input/focus/wheel/operations проверки; старые4x4geometry assertions заменены проверкой выбранной композиции.
- game/SelfTest.TowerMontage.cs — source/target, same-type modified swap, настоящие floor/inventory/whole currencies,18групп/2страницы и геометрия720/900.

SelfTest.TowerReference.cs прочитан и использован без изменений. baseline-source/ и baseline-source-hashes.json сохраняют исходный WIP; scoped-changes.patch содержит только этот этап относительно baseline, final-source-hashes.json фиксирует финал.

## Проверки

- `dotnet build game/Dialoid.Game.csproj --no-restore -p:NuGetAudit=false` — PASS,0warnings/errors.
- Godot4.7.2.NET, Vulkan Forward+, AMD Radeon RX6750XT: `DIALOID_SELFTEST_FILTER=tower` + `--path game -- --selftest --screenshots <absolute after>` — PASS16/16, финальный tower-gpu.log.
- Исходный `tower.reference` GPU — PASS2/2, baseline-reference.log и before/. Новый montage test сначала RED на старой композиции, затем PASS. UI-review regression сначала RED (review-regression-red.log), затем PASS в final16/16.
- Core Tower тесты выполняет root независимо; результаты не присваиваются исполнителю UI.

Основные кадры after/: reference-{720,900}-{empty,installed,selected-focus,gamepad,world-hud}.png; montage-{720,900}-modified-page{1,2}.png (18групп,16на первой); montage-real-floor-currency.png, montage-real-inventory-currency.png, montage-whole-tower-currency.png; activity-*-720.png; wheel-CurrencyGrid-720.png. Полноэкранная доска показана отдельно от мира/HUD. Кадры720/900 и насыщенный каталог просмотрены автором в оригинальном размере.

## Ревью и ограничения

Арт /root/skills_art: APPROVE по финальным720/900,16группам, focus/gamepad/wheel. Исправлены aspect/видимость витража и одинокие буквы названий. UI/code /root/tower_montage_ui_review: APPROVE после независимой проверки исправленной подписи и регрессии. Null floor даёт нейтральную подсказку установки, не обещает whole-tower crafting при выбранном инвентаре.

Названия каталога12px с ellipsis при нехватке места; полные названия доступны в detail/tooltip/accessibility. Длинные свойства, история результата и правая панель требуют вертикальной прокрутки. Основные actions, все5этажей, whole-tower, shuffle/check/summit, каталог и pager помещаются720 при16группах. Все ранги сохраняют число этажей из Rules.Floors (сейчас5), а не из номера ранга.

Существующие environment warnings: обычный restore/build упирался в NU1900 из-за недоступности api.nuget.org vulnerability feed; отключён только audit командным параметром, конфиги не менялись. Godot baseline/final пишет невозможность user://log rotation в sandbox, Failed to read root certificate store и shader-cache f.is_null; semantic GPU checks проходят. Исходный standalone git ls-remote не прошёл сеть; root отдельно получил main88712caf143e15bbe78b5b93072ee5ca3de54048. Физический геймпад не подключался: реальная GUI-навигация проверена инъекцией JoypadButton/Dpad/A, как в существующих самопроверках.

Финальный headless Tower: PASS16/16, tower-headless-final.log. Старый tower-headless.log13/16 сохраняет промежуточный сбой трёх inputtests: lookup GetChildren().OfType<Label>().Single() перестал быть уникальным после добавления ActivityName. Исправлен адресный GetNode<Label>(Count), проверки текста/выравнивания счётчика сохранены. Это не сбой игровых групп или teardown. Root независимо подтвердил build0/0, CoreTower186/186 и GPUtower16/16.

## Проверка интегратора

Root независимо повторил build0/0, CoreTower186/186, GPU16/16 и headless16/16. Кадры root-after/, логи root-gpu.log/root-headless.log, результаты и ограничения — integration-receipt.md. Все27Core/datafingerprints совпали с baseline.

