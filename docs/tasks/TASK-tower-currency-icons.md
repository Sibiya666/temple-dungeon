# TASK-tower-currency-icons

Статус: review (локальная реализация и независимая приёмка завершены)
Beads: dialoid-dqq (claimed)
Цель: заменить недостающий предметный арт расходников Башни, сохранив выбранный экран05 и существующие механики.
Основа: поручение владельца «Теперь обнови недостающие иконки», текущий игровой скриншот; ART_DIRECTION и утверждённая витражная серия активностей. Иконки через GPT по утверждённому исходнику; новых концептов/фонов не нужно.
Исполнители: /root/tower_currency_art (dialoid-technical-artist, assets); Godot wiring назначается отдельно; /root интеграция/документы. Независимый арт: /root/skills_art, UI reviewer отдельный.
Разрешённые файлы художника: game/Ui/Tower/Art/currencies/*.png, docs/art/tower/currency-icons-2026-11-10/ (промпты/исходники/provenance/размеры). Не менять C#/SVG/Core.
Предварительный scope:13реальных currency IDs, включая shuffle; служебные empty/tower/summit отдельно по арт-сверке. Не создавать отсутствующий в data reroll_variant. Не выдавать пустой слот за недостающий контент.
Утверждённый scope после сверки арт-куратора:15bitmap —13currencies и symbols/tower.png,summit.png. Empty_floor не генерировать: честный пустой cut-socket; confirm/close служебныеSVG остаются. Артисту разрешены дополнительно только2названныхsymbolPNG. Godot writer /root/tower_five_godot: TowerBoardArt.cs, SelfTest.TowerIcons.cs, минимальная registration SelfTest.Tower.cs, evidence ui-styleguide/2026-11-10-tower-currency-icons/; Montage.cs только shuffle/summit icon wiring безперестройки05, ReferenceStyle.cs только при необходимости удаления emptyfallback. Root владеет задачей/reference/integration docs.
Не входит: механики, данные, новый контент/узлы/skills, раскладка05, фоны, другие окна, платный API/новые расходы, main/PR delivery.
Готовность: точный ID→asset, сохранён original/ref/prompt/provenance,64/32/24px проверены, runtime720/900 и input/счётчики/состояния сохранены; build+targetTowerchecks; независимые арт/UI APPROVE. Не скрывать долг fallback.
Результат:15раздельныхGPTPNGпоутверждённымVenice/палитровымисходникам:13currencies+2symbols,128RGBA. Исходники/промпты/provenance/64-32-24экспорты: docs/art/tower/currency-icons-2026-11-10/. RuntimeточныеID,новыеshuffle/summitиhonestempty; layout05/фонсохранены. Независимые артasset+runtime иUI/code APPROVE. Rootbuild0/0,GPU18/18,headless18/18,27Core/dataфайловнеизменны. Evidence/узкийпатч/хэши: docs/art/ui-styleguide/2026-11-10-tower-currency-icons/. Свежаяиграоткрытавидимымокномпослеобновления. Коммита/main deliveryнет;Beadsin_progressсlocal-review-complete.
