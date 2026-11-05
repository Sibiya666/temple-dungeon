# TASK-tower-shuffle-completion: цикл Superpowers
Статус: done
Исполнитель: dialoid-core-developer; затем dialoid-godot-developer; основной агент — интеграция.
Цель: пустой старт башни и заполнение только явной кнопкой шафла.
Основа: docs/superpowers/specs/2026-11-01-tower-inventory-completion.md
План: docs/superpowers/plans/2026-11-01-tower-inventory-completion.md
Зависимости: существующие TASK-tower-inventory-nodes и TASK-tower-icons-frames
Разрешённые файлы: исполнители — только Files своих задач плана; интегратор — план, ledger, эта карточка, пакет ревью, docs/art/tower/2026-11-01-superpowers/.
Не входит: сохранения, 3D-этажи, баланс, новый арт, сторонние изменения.
Критерии готовности: Core и Godot red/green, независимые task reviews и финальный review, настоящие кадры и точный отчёт.
Проверка: по плану.
Ревью: отдельные экземпляры технического и UI/арт кураторов.
Результат: Core FillRandom и явная кнопка подключены. Башня стартует пустой; шафл переносит экземпляры текущего ранга только в пустые слоты; установленные активности и свойства сохраняются. Нехватка предметов/валюты и полная башня — атомарный отказ. Работают обе панели, переносы и намазки.

Task1: observed red6 failures, greenTower181/181; fullCore999/999, Sandbox144/144. Независимый taskreview Spec/Quality APPROVE.
Task2: observed red6/7 disabledshuffle, green7/7; строгая game build0ошибок,3NU1900. Интегратор снял cleanVulkan7/7, кадры empty-start/shuffled/720p: docs/art/tower/2026-11-01-superpowers/final/. Независимый taskreview Spec/Quality/UI-Art APPROVE.
Broad final review всей связки нашёл Important: фокус отменял прокрутку колесом обеих панелей; Minor: восстановление фокуса без проверки нового FocusMode. Оба исправлены отдельным автором и регрессионными тестами (red7/10 →green10/10). Интегратор принудительно пересобрал game после выявленного старого бинарника: Rebuild0ошибок,1NU1900. Последний cleanVulkan10/10: прокрутка Activity445/445, Currency352/352; клавиатура/пад возобновляют навигацию, фокус после перераскладки виден. Итоговый final-rereview APPROVE, новых замечаний нет. Кадры и log в docs/art/tower/2026-11-01-superpowers/final/ и final-engine.log.

Цикл: спецификация/план → CoreTDD → независимый Spec/Quality review → GodotTDD → независимый Spec/Quality/UI-Art review → broadreview → один fixround → scopedrereview → итоговая проверка. Подробная история в .superpowers/sdd/tower-inventory-completion/progress.md. Никаких коммитов/push/merge; чужие изменения сохранены. Боевые этажи, сохранения и реальные дропы вне работы. Четыре известных сбоя прежнего общего Godot-прогона не переоценивались, целевой10/10 не назван полной приёмкой игры.
