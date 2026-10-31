# TASK-tower-cursor: стилизованный курсор

Статус: done
Исполнитель: dialoid-godot-developer, экземпляр cursor_impl; основной агент — векторный арт и интеграционная проверка.
Цель: читаемый курсор башенного мира в работающей игре.
Основа: поручение владельца, docs/art/ART_DIRECTION.md, утверждённый Venice D4.
Зависимости: нет.
Разрешённые файлы: исполнителю game/Ui/Art/TowerCursor* и game/project.godot; основному агенту game/Ui/Art/Cursors/**, docs/art/cursor/** и эта карточка.
Не входит: Main.cs, RoomHudSkin, другие HUD/комнатные изменения.
Критерии готовности: тёмный металл и светлая кромка; точный hotspot у острия; читаемость на светлом/тёмном; системные текстовые/resize формы сохранены; сборка и запуск без ошибки курсора.
Проверка: сборка, импорт, проверочная сцена с реальной Main и контрастными образцами; hardware cursor отдельно показан TextureRect, так как он не попадает в viewport screenshot.
Ревью: независимый UI-куратор.
Результат: TowerCursor autoload, SVG 40×40 и проверочная сцена подключены. Build 0 ошибок/предупреждений; import exit 0; headless и GPU TowerCursorCheck exit 0, CURSOR_OK (native=True на GPU). Hotspot (3,3) непрозрачен и совпадает с остриём. Кадр docs/art/cursor/in-game.png, экспорт docs/art/cursor/tower-pointer.png. Независимый cursor_review (dialoid-ui-curator): APPROVE, блокеров нет. Чужие изменения комнаты, HUD и SelfTest не включены.
