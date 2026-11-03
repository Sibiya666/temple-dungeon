# Подключение одобренной пятёрки

Владелец после показа серии: «Супер, оставляем!». Существующие PNG навыков `cleave`, `battle_cry`, `fireball`, `ice_lance`, `spark` заменены байт в байт на утверждённые `../export/<skill_id>-64.png`. Новых генераций нет. Другие навыки, узлы, UI-код и их назначение не изменялись.

Предыдущие игровые PNG сохранены в `previous/`. `runtime-hashes.json` фиксирует SHA256 источника, итогового runtime PNG и предыдущей версии. `GLASS_ADOPTION_HASH_OK`: совпадение5/5. Пути runtime по-прежнему `res://Ui/Art/Icons/skills/<skill_id>.png`, каталог GameIcons не менялся.

## Проверки

- `dotnet build game/Dialoid.Game.csproj --no-restore`: exit0, ошибок0, предупреждений3NU1900 — недоступен источник NuGet audit.
- Godot `--headless --editor --import --path game`: exit0; пять PNG переимпортированы.
- Графическая `res://Ui/Art/IconIntegrationCheck.tscn -- --icon-screenshots <эта папка>`: exit0, `BUFF_ICONS_OK`, `EQUIPMENT_ICONS_OK`, `ICON_INTEGRATION_OK`. GPU AMD RX6750XT / Vulkan. Обычные кадры HUD, эффектов, симуляции и панелей сохранены рядом.
- Дополнительный `--script <абсолютный путь catalog_capture.gd>`: exit0, `GLASS_CATALOG_OK`. Скрипт только для съёмки: загружает настоящую Main, выбирает существующие эталонные билды через Tree.ItemActivated и открывает настоящие кнопки каталога. Не добавляет нарисованные поверх иконки и не меняет runtimeUI.

Все прогоны с отдельным APPDATA внутри `game/.godot/glass-adoption-*-user`, реальный пользовательский профиль не изменялся. Логи `.log` игнорируются правилами проекта. Известное sandbox-сообщение Godot `Failed to read the root certificate store` не помешало импорту/рендеру.

## Кадры настоящего UI

- `sorcerer-catalog.png`: Искра, Огненный шар и Ледяное копьё вместе в каталоге.
- `warrior-catalog.png`: Рассекающий удар и Боевой клич в каталоге и HUD.
- `skill-ice_lance.png`, `skill-fireball.png`, `skill-spark.png`: выбранное умение с настоящим деревом и HUD.
- `accepted-interface-icons.png`, `live-buffs.png`, `simulation-icons.png`: штатная интеграционная сцена.

Автор просмотрел каждый кадр каталога и кадр ледяного копья. Силуэты, подписи и точные игровыеID сохранены; новые64px остаются различимы при игровых масштабах. В24px внутренние переплёты закономерно упрощаются; мелкие исходные пробы сохранены в `../export/`. Деревья узлов и их существующие картинки этим подключением не обновлялись.

Независимый UI-куратор cursor_review: APPROVE для runtime-подключения одобренной пятёрки. Независимо сверены SHA2565/5 и реальные каталоги/HUD, включая ледяное копьё. Остальные общие символы узлов этим вердиктом не утверждаются как индивидуальные иконки.
