# Independent code review — unified UI/art imports

**ВЕРДИКТ: APPROVE.** Дата:11.11.2026. Ревьюер `/root/runtime_code_review`.

Scope: новая интеграция runtime art в ветке `codex/ui-art-unified-2026-11-11`: GameIcons.cs, GameIcons.Swordsmen.Generated.cs/.uid, SwordsmenIconsCheck.cs/.tscn/.uid, TowerCursor.cs, TowerCursorCheck.cs и добавленные PNG/import. Уже принятые seven-window и Character/Skills layout-изменения повторно не ревьюились. Production ревьюером не изменялся; собственные сборки/импорты не запускались.

## Стоимость и архитектура

| Сущность | Тип изменения | Затронуто |
|---|---|---|
| 191 недостающая node-картинка +17сохранённых | Godot resource lookup | Exact skill/node whitelist;208узлов12семейств |
| 6портретов Apostate | Архивные ресурсы | GameIcons.Skill и3родительских alias; active content не активируется |
| 3курсора48px | Godot UI + local preference | TowerCursor, отдельный user://cursor.cfg, F8 |
| Проверяющие сцены | Developer-only checks | Standalone scene loading, размерные листы, native cursor fixtures |
| Боевая модель/JSON/билды | Изменений нет | Core/Sandbox/data не затронуты |

## Проверено по коду

- DedicatedNodeArt проверяет одновременно skill ID и node ID до сборки resource path. Разные семейства с одинаковыми локальными node ID не смешиваются. Произвольные пути и traversal ID в новый каталог не проходят; незавершённые семейства сохраняют прежний Effects fallback.
- `GameIcons.Skill` оставляет прежние13портретов/старые alias и добавляет только перечисленные6архивныхAPпортретов/3alias. Загрузка ресурса не добавляет класс или skill в GameData. Checker дополнительно сверяет active Warrior IDs с замороженным набором и проверяет отсутствиеAP в активных classes/skills.
- Существующие17Cleave PNG и утверждённая витражная пятёрка не имеют diff. Generated partial не заменяет shared GameIcons целиком и сохраняет кэшированный Load через ResourceLoader.Exists.
- TowerCursor нормализует выбор тремя разрешёнными ID, проверяет размер48×48 до установки, кэширует только подходящие textures. Одинаковый выбранный портрет для Arrow/PointingHand — явная выбранная модель; Ibeam/resize и явно назначенные компонентом shapes не перезаписываются.
- F8 обрабатывается через `_UnhandledKeyInput` только для press без echo/modifiers, с guard текстового focus и видимых Popup. Сохранение относится только к отдельному локальному cfg; неисправный/неизвестный cfg безопасно возвращает default. Toast не принимает mouse/focus и автоматически скрывается.
- Подписки NodeAdded и кнопок снимаются в ExitTree; custom native cursor сбрасывается. Старые TexturePath/staticHotspot API не имеют живых callers в текущей runtime-ветке; сборка подтверждает актуальность call sites.
- Cursor checker использует GUID temp cfg, восстанавливает original SettingsPath/SelectedId в finally и не сохраняет preview-выбор в реальный профиль. Negative malformed cfg fixture намеренно вызывает Godot parse ERROR, после которого проверка продолжает safe fallback и заканчивается exit0/OK.
- Standalone checkers не меняют production Main/project/panels. Список импортов и происхождение отдельной device-ветки зафиксированы в `docs/art/artbook/2026-11-11/ui-art-integration-audit.md`; старые gameplay/host/HUD/Room файлы не переносились поверх новой реализации.

## Прочитанные доказательства

В этой папке непосредственно прочитаны:

- `build.log`:0warnings/errors;
- `headless.log`:46/46PASS;
- `gpu.log`:8/8PASS;
- `icons-headless.log` и `icons-gpu.log`:208точных64pxnodePNG+6APportraits, unsafe IDs rejected, active catalog unchanged;
- `cursor-headless.log` и `cursor-gpu.log`:3разных48pxdesigns, opaque hotspots, обычные/disabled/explicitbuttonshapes, F8 реальным PushInput, echo/text/modifierguards и isolated preferences; GPU native=True.

Это результаты исполнителя, прочитанные независимым ревьюером, а не заявленный собственный прогон.

## Важное, не блокирующее code imports

В `SkillsPanel.Graph.cs` остаётся прежнее условие `skill.Id != "cleave"` для текста «комплект смысловых иконок не завершён». После этого импорта пять остальных Warrior-семейств тоже имеют полный выделенный набор. Сообщение стало неточным: при ближайшей локальной правке следует определять долг по фактическому покрытию узлов, сохранив честный долг незавершённых Sorcerer/прочих семейств. Этот файл вне текущего narrow import scope, его ревьюер не менял.

## Ограничения

Иконный checker доказывает exact ID/resource path/размер/наличие и уникальность путей, а не художественную уникальность пикселей и смысловую читаемость каждой картинки; это предмет отдельного арт-ревью размерных листов/реальных кадров. Цифра214означает208node+6архивныхportraitресурсов, а не214новыхактивныхнавыков. Курсоры проверены движком на текущей Windows/GPU; другие DPI/ОС отдельно не сертифицированы. Merge/main delivery и коммерческие права заново этим code-review не подтверждаются.

Кодовых блокеров нет. Ядро остаётся независимым от Godot, правила/формулы/детерминизм и формат билдов этим импортом не изменяются.

## Scoped follow-up: точность сообщения о долге иконок

**APPROVE, замечание закрыто.** После авторизованной узкой правки повторно проверены `GameIcons.HasDedicatedSkillNodeArt` и условие в `SkillsPanel.Graph.cs`.

Helper возвращает true только для разрешённой пары skill/node и реально загружаемой dedicated texture; общий Effects fallback не принимается за завершённый арт. Панель теперь выводит долг через `skill.Tree.Any(node => !GameIcons.HasDedicatedSkillNodeArt(skill.Id,node.Id))`. Поэтому полностью покрытые Warrior-семейства не получают ложное предупреждение, а незавершённые Sorcerer/прочие семейства сохраняют честное сообщение.

Проверяющая сцена дополнена meaningful негативными wrong-skill/unknown/traversal парами, всеми208validпарами и настоящими панелями Crush (warning отсутствует) / Fireball (warning присутствует). После правки непосредственно прочитаны обновлённые `build.log`0/0, `icons-headless.log`208+6OK, `headless.log`46/46PASS и `gpu.log`8/8PASS. Курсорный код/ассеты и его прежние успешно прочитанные проверки этой узкой правкой не изменены.

Итог: **кодовый APPROVE подтверждён**, новых блокеров нет. Прежний раздел о stale warning описывает найденное и теперь исправленное замечание, а не текущий долг. Проверки не запускались ревьюером; исходники им не менялись. Арт/PDF- и repository/main-приёмка остаются отдельными стадиями.
