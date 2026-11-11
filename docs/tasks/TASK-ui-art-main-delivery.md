# TASK-ui-art-main-delivery

Beads:dialoid-g01. Статус:review до подтверждения merge.
Цель: по явному поручению владельца слить единую UI/art-ветку в GitHub main.
Область: публикация проверенной ветки, PR и штатный merge, Beads; единственная дополнительная кодовая правка — устаревший SelfTest.Room.cs, production/Core/data не меняются.
Приёмка: Core1004/Sandbox144, game137, GPU9, независимое review; evidence/2026-11-11-ui-art-main-delivery/.
Исполнитель:root интеграция; runtime_code_review независимое узкое ревью. Автор и ревьюер разные экземпляры.
Main защищён обязательным task-tower/pr-acceptance. Публикуется только фактический результат локальной приёмки, без отключения правил. После успешного remote merge проверить SHA standaloneкомандой владельца и закрыть g01/cjd/vec.
Исходная dirtyрабочаякопия, другиеветки и worktrees сохраняются. Forcepush, обходзащиты и удалениеветок не входят.
