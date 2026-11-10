# Окно навыков: runtime-эталон

Задача Beads `dialoid-tsa`. Канон — `docs/art/ART_DIRECTION.md`; первый общий лист — источник предложений, не замена канона. Новых генераций/платных запросов/навыков/узлов нет. Витражные PNG и самостоятельный Venice-задник сохранены без изменения.

## Что изменено

- Локальная тема окна навыков использует добавочные `RoomHudTheme.ReferenceWindowTheme` и `Heading`, именованные размеры текста и отступов. Другие окна не переключаются на новую тему.
- Каталог: квадратный портрет 80 px, плитка 112 px, промежуток 12 px; постоянное название и статус «Слот N», «Не назначен» или требуемый уровень. При 720p помещаются три колонки вместо двух.
- Деталь: название и портрет40 px первыми; назначение, уровень, возврат и дерево сохраняют настоящие session-команды. Закрытый навык просматривается, но назначение отключено с причиной.
- Существующий задник сохранён, под текстом/графом добавлено спокойное тёмное поле. Изменение WindowBackdrop ограничено skills.
- Выбор узла — замкнутая холодная линия; hover/gamepad target — две угловые метки. Цвет и масштаб рисунка не скачут. Золото не внедрено.
- Возврат B восстанавливает фокус прежнего портрета и видимость после сортировки flow. Убрано deferred восстановление scroll, перетиравшее автопрокрутку к фокусу.

## Настоящие кадры

`before/` — десять кадров до визуальных правок; `after/` — десять финальных кадров. `reference-{720,900}-{catalog,locked,detail,gamepad,catalog-focus}.png`.

`catalog-before-after.png` — сравнение настоящих окон720p в масштабе1:1; только crop/монтаж, интерфейс не дорисован. Воспроизведение: `& ./build-comparison.ps1` из этой папки.

Оговорка: baseline900p снят с сохранённой геометрией окна720p; в final capture selftest-конфигурация сбрасывается при каждом viewport, поэтому900p показывает штатную геометрию900p. Для прямого сравнения одинаковой геометрии используйте720p.

## Проверка

Из корня проекта, PowerShell:

```powershell
dotnet build game/Dialoid.Game.csproj --no-restore
$env:APPDATA = Join-Path (Get-Location) 'game/.godot/skills-reference-user'
$env:DIALOID_SELFTEST_FILTER = 'skills.'
& 'C:\Users\sibiya666\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path game -- --selftest
```

Сборка:0 warnings/errors. Skills:7/7 PASS, включая настоящие мышиные клики, browse без Revision, назначение/undo/очки/ротацию, gamepad A/B и видимость возвращённого фокуса.

Для финальной съёмки:

```powershell
$env:DIALOID_SELFTEST_FILTER = 'skills.reference'
& 'C:\Users\sibiya666\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --path game -- --selftest --screenshots 'C:/Users/sibiya666/Documents/project/dialoid/docs/art/ui-styleguide/2026-11-10-skills-reference/after'
```

Графический capture:2/2 PASS, Vulkan/AMD RX6750XT. Фильтр — целевая проверка, не полная приёмка игры. Полный журнал финальной съёмки `capture-final.log` (игнорируется git). Изображения просмотрены напрямую.

Дополнительные команды: тот же headless запуск с `$env:DIALOID_SELFTEST_FILTER = 'gamepad.'` и отдельно `'windows.'` (для Windows использован отдельный APPDATA `game/.godot/skills-reference-windows-user`). Итог:15/15 gamepad,7/8 windows. В GamepadFields исправлен устаревший вход в сценарий: после открытия каталога геймпадом A открывается назначенный навык, затем выполняются прежние assertions редактирования SpinBox. Проверки не ослаблены. Неустранённый сбой вне scope: `windows.layout` — минимум окна Персонаж637px/ширина655px превышает треть экрана. `git diff --name-only -- game/Ui/CharacterPanel.cs game/Ui/WindowHost.cs` пуст: окно Персонаж и host не менялись, но отдельный baseline-прогон ошибки не выполнялся; не выдаём это за доказанный результат baseline. Во всех запусках присутствует системное сообщение Godot `Failed to read the root certificate store`, не мешающее загрузке/рендеру.

## Границы приёмки

На720p текст выбранного узла находится ниже графа и требует прокрутки; на900p часть длинного описания также может быть ниже viewport. Полный redesign detail не входит в этот проход. Существующий долг индивидуальных смысловых иконок узлов этим изменением не закрывается: готовность оболочки не равна готовности всех ассетов контента. Runtime-проверка использует настоящие существующие картинки; fallback не объявляется новой готовой иконкой.

Независимые ревью: `/root/skills_art` APPROVE художественной части; `/root/skills_ui_review` APPROVE итоговых кадров, UI/code diff и узкого исправления GamepadFields. Интегратор независимо повторил сборку 0/0, Skills 7/7 и Gamepad 15/15, просмотрел кадры и сравнение. Сопутствующие чужие Tower-изменения не изменены. Это локальная приёмка: main/PR не поставлялись. Beads не поддерживает review без изменения конфигурации, поэтому dialoid-tsa остаётся in_progress с записью локальной приёмки в notes.
