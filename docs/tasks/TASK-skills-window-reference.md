# TASK-skills-window-reference: эталон окна навыков

Статус: review (локальная реализация и независимое ревью завершены; поставка в main не выполнялась)
Beads: dialoid-tsa (создана и взята через --claim; владелец проверен через show)
Исполнитель: dialoid-godot-developer /root/skills_implementation; интегратор /root
Цель: проверенный в игре эталон окна навыков и адаптация oil-ui для Dialoid.
Основа: поручение владельца 2026-11-10; ART_DIRECTION; спецификация `docs/superpowers/specs/2026-11-10-skills-window-reference-design.md`.
Зависимости: нет для оболочки; существующий долг смысловых иконок узлов учитывается отдельно и не скрывается.
Разрешённые файлы: по Ownership в плане `docs/superpowers/plans/2026-11-10-skills-window-reference.md`; дополнительно только GamepadFields в `game/SelfTest.Gamepad.cs` для штатного входа каталог → detail перед прежними asserts редактирования.
Не входит: новый контент, генерации, глобальная переделка окон, изменения Tower, коммит/публикация.
Критерии готовности: baseline/after, реальные целевые размеры, рабочие команды и фокус, независимое ревью, документация эталона.
Проверка: сборка, целевые selftest Skills/gamepad, визуальное сравнение PNG.
Ревью: dialoid-art-curator и dialoid-ui-curator; code review отдельным экземпляром.
Результат: oil-ui 0.33.3 установлен без изменения upstream; адаптер `.agents/skills/dialoid-ui/SKILL.md` принят независимым UI-куратором. Эталон описан в `docs/art/UI_REFERENCE.md`; кадры/команды в `docs/art/ui-styleguide/2026-11-10-skills-reference/README.md`. Сборка 0/0, Skills 7/7, Gamepad 15/15, графическая съёмка 2/2. Арт `/root/skills_art` APPROVE, UI/code `/root/skills_ui_review` APPROVE. Root повторил сборку, Skills и Gamepad. Дополнительный Windows 7/8: ширина окна Персонаж вне scope; baseline этого сбоя не запускался. Долг иконок узлов и прокрутка detail явно сохранены. Beads dialoid-tsa зарегистрирована/claimed; поставка в main не выполнялась, задачу не закрываем как repository done.
