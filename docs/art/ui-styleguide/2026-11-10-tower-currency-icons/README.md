# TASK dialoid-dqq — runtime иконок Башни

Комплект производства: docs/art/tower/currency-icons-2026-11-10/, художник /root/tower_currency_art. Runtime evidence и C# интеграция: /root/tower_five_godot. Изменения локальные, без коммита и без заявления о main/PR delivery. Новые механики/данные/навыки/узлы/расходы не добавлялись.

## Точная интеграция

TowerBoardArt.Currency направляет13реальных data ID на currencies/{id}.png: imbue, imbue_type, imbue_tower, clear, shuffle, reroll, upgrade, remove, biased_rare_elite, biased_rare_reward, biased_epic_elite, biased_epic_reward, bias_value. Symbol(tower|summit) направляет на symbols/{id}.png. Unknown currency даёт null; реальные ID не используют SVG fallback. Legacy reroll_variant.svg оставлен только для совместимости, его нет в текущих данных.

Activity(null|empty_floor) возвращает null: пустой выбранный этаж и невыбранный источник показывают честное пустое срезанное гнездо. В route пустые гнёзда уже были без мотивов. Другие служебные символы не менялись.

В12реальных кнопках валют сохранены24×24px TextureRect, hit target60×44px и счётчик11px. Shuffle получает24pxPNG; его локальные padding3pxпо вертикали/4pxпо горизонтали сохраняют высоту существующей команды. Вершина получает24pxPNG в той же строке перед прежним текстом. Шрифты, выбранный фон/геометрия05, правила и управление сохранены.

Изменены только game/Ui/Tower/TowerBoardArt.cs, game/Ui/Tower/TowerBoardView.Montage.cs, game/SelfTest.Tower.cs (одна регистрация), новый game/SelfTest.TowerIcons.cs. ReferenceStyle.cs не менялся. PNG/SVG интегратор не редактировал. scoped-code-changes.patch построен от before-source; before/final-source-hashes.json фиксируют состояние. core-data-before/after и mechanics-unchanged.json подтверждают27/27 неизменных файлов механик/данных.

## Проверки

До подключения новый тест был RED на imbue.svg вместо точного PNG: runtime-red.log. Baseline before/ содержит реальные720/900.

- Godot4.7.2.NET --headless --path game --import: exit0, import-final.log.
- dotnet build game/Dialoid.Game.csproj --no-restore -p:NuGetAudit=false: PASS0warnings/errors.
- DIALOID_SELFTEST_FILTER=tower, Godot --path game -- --selftest --screenshots <absolute after>: GPU Vulkan Forward+ AMD RX6750XT PASS18/18, tower-gpu.log.
- Тот же target --headless --path game -- --selftest: PASS18/18, tower-headless.log.

Новый tower.currency-icons-runtime проверяет точные PNG paths всех13+2, уникальность ресурсов/рисунков64/32/24, runtime alpha, минимальное исходное разрешение64,45runtime PNG exports, назначение каждого реального TextureRect, счётчики кошелька,24px размеры и enclosure hit target, реальную иконку Shuffle/вершины, честный empty и unknown null. Существующие mouse/key/pad/focus/wheel/operations и новый GUI RemoveOne/full-status также проходят. Фильтр Tower не означает полный suite проекта.

## Кадры и происхождение

Основные after/currency-icons-720.png и currency-icons-900.png: базовая доска с12валютами, Shuffle/вершиной и честными пустыми гнёздами. after/reference-{720,900}-gamepad.png показывает whole-tower portrait и фокус. Там же installed/selected-focus/empty/world-hud,16групп/2страницы, реальная намазка и раскрытые подробности. after/runtime-icons/ содержит45экспортов15иконок64/32/24px, полученных из runtime texture. Автор просмотрел реальный720base и900whole/gamepad, рисунки не перекрывают счётчики.

Оригиналы, утверждённые референсы, промпты/метаданные генераций и производственные размеры — в каталоге художника docs/art/tower/currency-icons-2026-11-10/. Независимый asset-level ART APPROVE15/15 получен; runtime/контекст отправлен /root/skills_art для финального ревью. Root выполняет независимую интеграционную проверку.

## Ограничения и существующие предупреждения

Иконки валют показываются в24px, как прежде; смысл проверяется дополнительно по имени/предпросмотру. Физический контроллер не подключался: существующие проверки вводят реальные JoypadButton/Dpad/A в GUI. Длинные свойства и результаты доступны через прежние раскрываемые подробности/прокрутку.

Существующие sandbox/user settings/log rotation/root certificate/shader-cache сообщения сохранены в логах. Импорт завершился0, тесты проходят. Конфиги ради предупреждений не менялись. NuGetAudit отключён только командным флагом из-за недоступного внешнего feed. Частичный8PNG импорт partial-import.log не является готовым комплектом; финальный статус относится ко всем15после import-final.log. Product/test freeze после финальных18/18.

## Итоговая независимая приёмка

Арт asset+runtime и UI/code: APPROVE. Root повторил build0/0, GPU18/18, headless18/18;27Core/dataфайлов неизменны. Актуальные собственные кадры root-after/, логи root-gpu.log/root-headless.log и подробности root-receipt.md. Свежий видимый экземпляр Башни запущен с новыми ресурсами. Это локальная готовность, без коммита/main delivery.

