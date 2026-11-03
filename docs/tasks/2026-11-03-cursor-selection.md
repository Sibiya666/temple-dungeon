# TASK-cursor-selection: три курсора с сохранением выбора

Статус: done
Исполнитель: dialoid-godot-developer cursor_impl; root — runtime PNG и проверка.
Цель: все три выбранных концепта в игре, витраж по умолчанию, удобное переключение для теста.
Основа: прямое поручение владельца, Venice-358; предыдущие SVG отвергнуты.
Разрешённые файлы: исполнителю game/Ui/Art/TowerCursor.cs и TowerCursorCheck.cs; root — game/Ui/Art/Cursors/{glass,spear,key}.png и .import, docs/art/cursor/2026-11-03-selection/**, эта карточка.
Не входит: Main, TopBar, геймплей, чужие правки, новая генерация.
Поведение: F8 циклически витраж → наконечник → ключ, короткая подпись варианта, сохранение в отдельном user://cursor.cfg. На Arrow и PointingHand выбранный PNG, системные IBeam/Resize и MouseMode не трогать. Первое включение — витраж.
Проверка: PNG48×48 и hotspot на рабочем конце; выбор/цикл/загрузка/неизвестный ID/битый конфиг, echo/input guards, native GPU и headless, реальная Main и кадры трёх вариантов. Тесты не меняют пользовательские настройки.
Ревью: независимый UI-куратор.
Результат: три48pxPNG в runtime, витраж по умолчанию, F8цикл и сохранение в отдельномcfg. Build0ошибок/3сетевыхNU1900; import/headless/GPU exit0; CURSOR_OK, CURSOR_SELECTION_OK, CURSOR_BUTTONS_OK, CURSOR_F8_OK. Кадры всех трёх в docs/art/cursor/2026-11-03-selection/. Независимый cursor_review: APPROVE, блокеров нет. Пользовательский профиль тестами не менялся. По дополнительному поручению владельца сохранены STAINED_GLASS_EXPLORATION.md и ссылка в ART_DIRECTION.md: попробовать витражные иконки, интерьеры и задники от указанного оригинала осколка.
