# Character / Skills — independent technical review, pass 1

ВЕРДИКТ: REVISE до закрытия текущих регрессионных проверок. Beads: dialoid-vec, dialoid-5u9, dialoid-eo6.

Дата:11.11.2026. Ревьюер `/root/runtime_code_review`. Это новый этап, сравнение выполнено с `source-before/game__*.cs` и `source-before-hashes.json` в этой папке, а не с Git HEAD: принятая работа семи окон уже была незакоммиченным prerequisite и не переоткрывается этим ревью. Прочитаны новый spec/plan, текущие четыре panel-файла, новые charskills checks и узкий shared-host delta. Собственные сборки/импорты и исходные правки ревьюер не выполнял.

## Стоимость

| Область | Тип | Затронуто |
|---|---|---|
| Character settings/stats | Godot presentation | CharacterPanel, существующие NumberLabel/session commands |
| Skills catalogue/tree/detail | Godot presentation | Три SkillsPanel partials, существующий SkillTreeGraphView |
| Expand и фон двух окон | Godot presentation/resources | Allowlist, два PNG, capture checks |
| Правила/контент/сохранённые билды | Без изменений | Core, Sandbox, data не меняются |

## Текущие блокеры приёмки

1. Интегратор сообщил первый targeted результат39/43: margin Character NumberLabel отличается от действующего контракта4px. Исправить только локальное оформление этих чисел и повторить прежний assert, не менять эталон теста ради новой темы.
2. Та же группа выявила gamepad reachability Skills: назначение/снятие очка не достигаются в новой layout-структуре. Автор Skills уже исправляет. Нужен повтор исходных reachability сценариев с видимым фокусом и рабочими действиями, включая узкий viewport и нижние прокрученные controls.
3. Нужны финальные GPU720/900 с реальной видимой областью графа, Character controls/stats и возвратом catalogue focus/scroll. Одни width bounds в новом `CheckCharacterSkillsBounds` не доказывают вертикальную видимость или достижимость нижних действий.

## Проверенные неизменённые контракты

- Character выбирает класс/уровень/восхождение/оружие/предметы прежними session-командами; ApplyLevel остаётся deferred, dragging не перестраивает control посередине жеста.
- NumberFor/Numbers/LineNumbers/ItemBoxes и live CharacterLines сохраняются, строки статистики не подменены демо-данными. Смена класса по-прежнему явно предупреждает о сбросе билда.
- Skills сохраняет locked browsing без Revision, assignment/undo, слот и point rules. Новая компоновка не переписывает SkillPositions/links/routing и exact asset mapping.
- Focus override находится только в SkillTreeGraphView: базовый TreeView не меняется этим этапом. После base focus handling восстанавливаются прежние zoom/pan; явные пользовательские zoom/pan команды сохраняются.
- Content остаётся тем же SessionPanel; adaptive layout встроен внутрь его scroll, без прослойки между WindowHost и IPadPanel.
- Shared allowlist расширяется только Character/Skills; прежний unaffected-assert переносится на Passives по новому авторизованному scope, а не удаляется.

## Важное, не установленный блокер

- Character.RememberFocus восстанавливает control по Name. LevelSlider, AscendancyChoice, WeaponChoice, items/stats имеют стабильные имена; classBox пока без явного имени, поэтому путь по автоимени после смены класса ненадёжен. Стоит дать ему стабильный ClassChoice и проверить focus после class reset, не меняя команду.
- При графическом redraw Skills новый graph восстанавливает manual transform только при почти прежнем Size; изменение viewport ожидаемо вписывает граф. Финальная проверка должна различать изменение viewport и обычный rank redraw: второе не должно сбрасывать pan/zoom/selection.

## Граница

Независимых дополнительных source-блокеров сверх уже обнаруженных targeted failures на этом проходе не найдено. Требуется scoped re-review после исправлений и финальных логов. Локальная приёмка не равна поставке main.

# Scoped re-review Character/Skills — 11.11.2026

**ВЕРДИКТ ПО КОДУ: APPROVE.** Сравнение вновь выполнено с source-before snapshot этого этапа; принятая реализация семи окон не переоткрывалась. Source edits, сборки и импорт ревьюером не выполнялись.

## Предыдущие пункты

1. **NumberLabel margin — закрыт.** Character локально дублирует унаследованные state styleboxes и возвращает исходные content margins из DefaultTheme. Исходные Text/Line/API не подменяются. Native WordSmart + NoTrimming дают высоту переноса; ClipText ограничивает только фактическую область. В новых runtime проверках высота полного текста дополнительно измеряется шрифтом при реальной ширине.
2. **Skills gamepad reachability — закрыт по коду и прочитанным тестам.** Локальная явная цепочка строится заново после появления/перерисовки/смены selection. Первый Top и последний Bottom оставлены пустыми, чтобы цепочка не замыкалась и позволяла добраться до заголовка. Справа от graph ведёт явный entry в detail. Host уважает neighbour только если он разрешается в действующий target текущего root; disabled, stale path и outside-root проверены с возвратом обычной пространственной навигации. Никаких глобальных изменений TreeView этим этапом нет.
3. **Focus/selection — проверено.** Стрелки и PadSelect меняют только focus corners, A выбирает узел, а изменение очков остаётся отдельной session-командой. Новые assertions прямо проверяют неизменность Revision и доступного действия при перемещении фокуса.
4. **Graph visibility — закрыт кодом scoped subclass.** SkillTreeGraphView подстраивается под реальный Resize, включая изменение доступной высоты при pad footer; focus enter сохраняет прежний manual transform. Ранговый redraw восстанавливает zoom/pan при прежних размерах, а изменение viewport вписывает новый прямоугольник. Старый radial-graph regression остаётся включённым.
5. **ClassChoice — моё прежнее предположение снято.** Явное `Name = "ClassChoice"` присутствует и в source-before, и в текущем коде; отсутствие имени не было реальным дефектом. LevelSlider и другие названные controls восстанавливаются через прежний deferred redraw без изменения class-reset команды.
6. **Live numbers и context сохранены.** Skills сокращает только повторяющийся префикс в отображаемом Text внутри уже названной панели; исходные NumberLine, context и tooltip для Breakdown остаются. Character сохраняет live CharacterLines и выводит предупреждение нарушений также вверху, не выдумывая рассчитанные значения для невалидного билда.

## Прочитанные доказательства

- `root-build.log`: сборка0warnings/errors.
- `final-headless.log`: C targeted original-order group46/46 PASS; `focus-followup.log`:7/7 PASS, включая radial-graph и reachability.
- `root-gpu.log`: независимый GPU5/5 PASS после последних визуальных исправлений. Проверки включают весь rect каждой иконки/ранга внутри пересечения с настоящим scroll ancestor и высоту полного NumberLabel текста.
- Самостоятельно открыт финальный `root-after/charskills-720-skills-pad.png`: graph и правое действие видимы, focus/selection различимы, длинное описание переносится.

Результаты выше прочитаны из файлов, а не объявлены собственным тестовым прогоном ревьюера. Если интегратор повторяет общую46-сценарную группу после последних native-wrap правок, её новый итог должен быть приложен отдельно; старый лог нельзя переименовать в независимый новый прогон.

Новых кодовых блокеров не осталось. Финальная общая runtime-приёмка также требует отдельных арт/UI verdict. Core/Sandbox/data не изменены; main delivery не подтверждалась.
