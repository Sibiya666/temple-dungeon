# TASK-sword-hand-cursors: меч и латная рука

Статус: done
Исполнитель: dialoid-godot-developer, cursor_impl; основной агент — SVG-арт.
Цель: меч вместо Arrow, латный указательный палец вместо PointingHand.
Основа: прямой выбор владельца после первого курсора, ART_DIRECTION.md.
Разрешённые файлы: исполнителю game/Ui/Art/TowerCursor.cs и TowerCursorCheck.cs; корню game/Ui/Art/Cursors/tower-sword.svg, tower-hand.svg, docs/art/cursor/** и эта карточка.
Не входит: геймплей, MouseMode, IBeam/Resize, HUD, Main.
Критерии: различимые формы 40px, hotspot у острия/пальца, контраст светлый/тёмный, графический запуск без ошибок.
Проверка: build/import, TowerCursorCheck headless и GPU, новый кадр и PNG обоих ресурсов.
Ревью: независимый UI-куратор.
Результат: две SVG40×40 подключены; обычные BaseButton получают руку, исходные специальные формы сохраняются. Hotspot меча (3,3), пальца (12,3). Build 0/0, import exit0, headless/GPU exit0, CURSOR_OK и CURSOR_BUTTONS_OK. Кадр docs/art/cursor/in-game-forms.png; экспорты tower-sword.png и tower-hand.png. Независимый UI-куратор cursor_review: APPROVE, блокеров нет. Изменение Disabled под неподвижной мышью учитывается на следующем MouseEntered; polling не добавлен.
