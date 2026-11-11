# Independent review — shadow-budget test correction

**ВЕРДИКТ: APPROVE.** Дата:11.11.2026. Ревьюер: `/root/runtime_code_review`.

Narrow scope: только новая правка `game/SelfTest.Room.cs`, метод RoomShadowBudget. Исходники ревьюер не менял и тесты не запускал; отчёт записан отдельно. Ранее принятые UI/art imports и окна этим проходом повторно не оцениваются.

## Проверено

- Production `game/Room/RoomView.cs` побайтно соответствует main9a5f95c: Git blob текущего файла и `9a5f95c:game/Room/RoomView.cs` одинаковы — `b1b4b886d518806c1433d452d11dd2d28121fb2a`.
- Действующий production-контракт уже резервирует два источника: HeroLight и HighOpeningLight. `UpdateShadowBudget` на High включает opening shadow и выделяет `MaxShadowLights - 2` факелам; MaxShadowLights=8. Это существующее правило, не новая уступка тесту.
- Прежний тест ожидал до7факелов, а `ShadowLights` перечислял только OmniLight3D, пропуская SpotLight3D высокого проёма. Новый тест перечисляет реальные shadow-enabled Light3D всего Room subtree и отдельно требует наличие HeroLight и HighOpeningLight.
- Общий бюджет8 не ослаблен: теперь в него действительно включён SpotLight3D. После исключения двух обязательных fixed lights torch budget6 соответствует принятому production-контракту.
- Сохранены проверки: все остальные shadow lights принадлежат TorchLights, torch opacity0.5…0.7, радиус+гистерезис, минимальное заполнение доступного бюджета, неизменность состояния при повторном UpdateShadowBudget, наличие хотя бы одного shadow torch и факт достижения лимита в одной из контрольных точек.
- Production Room/Core/data не меняется этой правкой. Новых игровых правил и обходов графической проверки не добавлено.

## Граница

Это корректировка устаревшей модели наблюдения в selftest, а не отключение failing assertion ради merge. Полный повтор137Godot selftests и build после правки — ответственность интегратора; их успешное завершение должно быть приложено к delivery evidence до merge. Кодовый APPROVE не подменяет результат ещё выполняющегося прогона и не утверждает, что merge уже выполнен.

Неблокирующее: текущая диагностическая строка по-прежнему пишет «+ свет героя» и не упоминает opening; это лишь неполная подпись лога, не ошибка нового расчёта или assert.
