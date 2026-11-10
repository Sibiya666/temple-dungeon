# TASK-tower-ui-reference: единое оформление доски Башни

Статус: review (локальная реализация интегрирована и проверена; без поставки в main)
Beads: dialoid-2tt (claim и show выполнены)
Рабочая копия: C:/Users/sibiya666/.codex/worktrees/tower-ui-reference/dialoid
Исполнитель: dialoid-godot-developer /root/skills_implementation; интегратор /root
Цель: реальная доска Башни и детали активности в единой системе холодных металлических рамок, без изменения механик.
Основа: поручение владельца 2026-11-10; ART_DIRECTION; dialoid-ui/oil-ui; UI_REFERENCE; утверждённые иконки и формы из tower-activities-ku0.
Зависимости: зрелая рабочая реализация dialoid-ku0 используется как snapshot исходного состояния; перенос её Core/data WIP в main не входит в эту задачу.
Разрешённые файлы: в изолированной рабочей копии TowerBoardView.cs (только hooks/style), новый TowerBoardView.ReferenceStyle.cs, TowerBoardArt.cs, TowerGlassWindow.cs, activity_window.gdshader (frame decode), добавочные helpers RoomHudTheme.cs, при необходимости Art/cold-focus.svg; новый SelfTest.TowerReference.cs и одна регистрация в SelfTest.cs. Evidence docs/art/ui-styleguide/2026-11-10-tower-reference/. Интегратор владеет этой карточкой, spec/plan и дополняет UI_REFERENCE.
Не входит: контент/механики, генерации, расходы, изменение чужого WIP, копирование секретов, полная интеграция Башни в main.
Критерии готовности: один спокойный внешний контур, тонкие внутренние разделители; срезанные симметричные ноды, сохранённые сирень/жёлтый/белый; различимые выбор/наведение/фокус; идентификатор активности в detail; истинные причины недоступности и рабочие операции.
Проверка: baseline и after 720/900, мышь/клавиатура/геймпад, выбранное/пустое/disabled, количество/свойства/установка/возврат, сборка и Tower selftests, независимое арт/UI/code review.
Ревью: dialoid-art-curator /root/skills_art; отдельный UI-куратор.
Результат: UI-only патч из 9 файлов после независимых APPROVE арт `/root/skills_art` и UI/code `/root/skills_ui_review` применён интегратором в актуальную `tower-activities-ku0` при совпадении 109 baseline-записей и успешном apply --check. Там повторены build 0/0, import, Tower 15/15 clean и GPU 2/2 (реальные 720/900). 99 чужих snapshot-файлов остались побайтно неизменны; 9 UI-файлов совпадают с проверенным кандидатом. Core Tower baseline 186/186. Evidence/patch/receipt: docs/art/ui-styleguide/2026-11-10-tower-reference/ в рабочей копии Башни. Main/gameplay WIP не объединялись и не коммитились. Beads остаётся in_progress с local-review notes, потому что review не настроен и repository/main delivery не выполнялась.

Ограничения: нижние этажи и длинные свойства требуют прокрутки в 720p. Синтетический дополнительный focus/device-сценарий нового теста выполняется на GPU, поскольку его headless-комбинация после прежних 14 шагов давала Image/DummyTexture exit warning; итоговый headless 15/15 clean, существующие input/focus tests не ослаблялись. Исправлением production leak это не объявляется. Нового контента/генераций/расходов нет.
