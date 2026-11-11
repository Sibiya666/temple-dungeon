# UI/art — приёмка перед слиянием в main

Beads: dialoid-g01. Разрешение владельца: «слей» после поставки общей ветки codex/ui-art-unified-2026-11-11. Base main:9a5f95c; UI/art/artbook commit77c2d2f.

Повторная локальная приёмка:

- Core:1004/1004; Sandbox:144/144, core-tests.log.
- Game build:0предупреждений/ошибок, build-followup.log.
- Полный Godot headless без фильтра:137/137 PASS, full-headless-final.log.
- GPU room.shadow-budget|charskills|sevenwindows:9/9 PASS, gpu-final.log.
- CODE-REVIEW.md: независимое APPROVE узкой корректировки теста. Основные UI/art и артбук уже имеют независимые APPROVE в своих evidence.

Первый полный прогон136/137 выявил старую ошибку ожидания в room.shadow-budget. RoomView и исходный тест совпадают с main по Git blob. Рендерер резервирует два источника (герой и высокий проём), тест считал только OmniLight и ожидал семь факелов. Исправлен только SelfTest.Room.cs: учитываются все Light3D, проверяется наличие проёма, общий лимит8 и выделение6мест факелам. Проверки радиуса, opacity, гистерезиса и заполнения бюджета сохранены. Production комнаты/Core/data не менялись.

137/137 — полный игровой прогон;9/9 — явно ограниченная GPU-группа, не полный GPU-набор. Статус task-tower/pr-acceptance представляет эту фактическую локальную приёмку с открытыми логами, не выдаётся за GitHub Actions. Правила main не отключаются; доставка через PR с проверкой точного head SHA. Фактический mergeSHA и URL записываются в Beads после подтверждения GitHub.
