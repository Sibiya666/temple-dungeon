# Коррекция соответствия выбранному варианту05

Владелец указал на потерю задников и большое отличие от выбранного макета. Прежний after/ и его ревью сохранены как история и не служат финальной визуальной приёмкой. Рабочий первичный референс этой коррекции — docs/art/tower-seven/shots/native/v05.png, выбранный «Монтажный пролёт»; уточнённый HTML tower-selected — дополнительный ориентир. Новых генераций, ассетов, расходов и механик нет.

## Причина и исправление

Прежний Montage заменил точный архитектурный оригинал почти невидимым ascent-v2 и перекрыл его непрозрачными ReferenceSurface. Нарушились вертикальный центр, порядок этажей, последовательность кнопок и место расходников. Теперь fullscreen использует точный res://Ui/Tower/Art/activity-window.png из HTML kit, пропорциональный cover с center38% и горизонтальной тёмной маской. Внешняя оправа прозрачная, центральная поверхность пропускает архитектуру. Три новые панельные подложки не добавлялись: такой композиции нет в выбранном05.

Восстановлены Georgia serif локально для бренда/номера этажа, этажи1→5 с отдельным номером/28px портретом/двумя строками и левой линией выбора; центральный вертикальный86px октагон, identity/биом/слоты, primary380px холодной сине-серой заливки и remove под ним; нижний каталог8 подписанных иконок с полными именами10px как у native, правый source64px и все12 реальных намазок в базовом720. Для16групп используется компактный центральный портрет48px при тех же командах и двух строках каталога. Начальные игровые состояния и модель данных сохранены.

Реальные дополнительные сведения доступны через фокусируемую кнопку «Цель / качество / свойства»: полный результат последней операции, подробности выбранного этажа и действительной цели, прогноз качества, свойства и адресный selector RemoveOne. Короткая строка результата в редакторе использует ellipsis и полный tooltip; точная полная копия находится в раскрытых подробностях и обновляется после операций. Статус не теряет стоимость/остаток. Source и currency target остаются независимы. Все13 data currencies сохранены:12 кнопок плюс Shuffle.

## Scope и доказательства

Изменены только TowerBoardView.cs, TowerBoardView.Montage.cs, TowerBoardView.ReferenceStyle.cs, SelfTest.Tower.cs, SelfTest.TowerMontage.cs. TowerGlassWindow.cs в этой коррекции не изменялся. before-source/ содержит состояние до коррекции; scoped-changes.patch — узкий diff от него; final-source-hashes.json — итог. Core/data/theme/Main/project/asset manifest/другие окна не менялись. Старые evidence не перезаписаны.

Сборка: dotnet build game/Dialoid.Game.csproj --no-restore -p:NuGetAudit=false, PASS0warnings/errors. Godot4.7.2.NET Vulkan AMD RX6750XT: DIALOID_SELFTEST_FILTER=tower, --path game -- --selftest --screenshots <absolute after>. Финальные логи tower-gpu.log и tower-headless.log. Новый тест tower.montage-details-input использует настоящие Tab/Enter для раскрытия, OptionButton popup/Home/Enter для адресного свойства и Tab/Enter для RemoveOne; проверяет удаление ровно одного свойства за один расходник, полную строку результата/расход/остаток и чтение полного результата после возврата к кнопке подробностей. Остальные реальные input/focus/wheel/operations сохранены.

Промежуточный tower-gpu-intermediate-wheel-fixture.log сохраняет16/17: старый wheel fixture требовал overflow у базовой панели, в которой теперь все12 намазок правильно помещаются. Fixture раскрывает реальные quality/mod details, затем выполняет прежние wheel-end/focus-return/selection-unchanged проверки и возвращает исходное состояние раскрытия. Проверки не удалялись.

after/: reference-720/900-empty,installed,selected-focus,gamepad,world-hud; montage-720/900-modified-page1,page2 (18групп/16первая); montage-720-expanded-target-remove-input; montage-720-full-result-via-details; wheel-CurrencyGrid-720. Полноэкранная доска и мир/HUD снимаются отдельно. Автор просмотрел реальные720/900, насыщенный каталог и полный результат.

Независимый арт /root/skills_art: FINAL PARITY APPROVE после сравнения с native05. UI /root/tower_montage_ui_review: parity визуально APPROVE, замечание о полной строке результата исправлено; финальная проверка замечания запрошена. Итоговую интеграцию и повторный root run записывает root отдельно.

Ограничения: длинные реальные свойства и результат требуют раскрытия/вертикальной прокрутки; dense16group режим компактнее native8. Управление pad проверено настоящим GUI с инъекцией Dpad/A, физический контроллер не подключался. Существующие sandbox log-rotation/certificate/shader-cache warnings сохранены в логах; конфиги ради них не менялись. NuGetAudit отключён только командным флагом из-за недоступного внешнего feed.

Финальный авторский GPU run после полного результата и проверки его области чтения:17/17 PASS, процесс завершён с exit0. Headless до добавления последнего дополнительного assertion области чтения:17/17 PASS (product code тот же). Root получил финальные ART и UI/code APPROVE. Product/test code freeze; новые scoped patch/hashes актуальны. Последний root integration run фиксируется отдельно.
