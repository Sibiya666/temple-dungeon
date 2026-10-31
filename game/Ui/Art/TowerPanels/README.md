# Тематические задники доски башни

2026-10-31. Три самостоятельных PNG для выбранного владельцем серого каменного интерфейса. Созданы встроенным `imagegen`; это оригинальные иллюстрации, без текста, кнопок и зашитой геометрии интерфейса.

| Файл | Панель | Мотив |
|---|---|---|
| `activities-v1.png` | Активности, слева | Готический зал с арочными входами |
| `ascent-v1.png` | Восхождение, центр | Взгляд вверх внутри древней башни |
| `inscriptions-v1.png` | Свойства и расходники, справа | Каменный стол, карта, печати и чернила |

Разрешение: 1024×1536 каждый. Верх содержит сюжетный мотив, низ — спокойный темный камень для плотного текста. Задники не заменяют иконки свойств, состояние выбора или предупреждения.

## Подключение

- Рисовать внутри рамки соответствующей панели, отдельно от прокручиваемого содержимого: фон остается на месте при перемещении списка.
- Для `TextureRect`: `ExpandMode = IgnoreSize`, `StretchMode = KeepAspectCovered`, `MouseFilter = Ignore`, `FocusMode = None`; ограничить отрисовку прямоугольником панели.
- Сверху расположить существующие элементы управления. Под текстом оставить темные подложки, ориентировочно 70–85% непрозрачности; итоговую читаемость проверять на 720p и 1080p. Это отправная точка оформления, не проверенное значение контраста.
- Не растягивать иллюстрацию как 9-slice рамку: сами каменные/железные рамки остаются отдельными ресурсами.

## Статус интеграции

При подготовке текущий `main` уже содержал новую 3D-комнату, а `game/Ui/Tower/TowerBoardView.cs` и экран доски отсутствовали во всех найденных рабочих деревьях. Поэтому этот пакет подготовлен отдельно. Пример наложения на сохраненный кадр — художественное превью, не новый runtime-скриншот и не свидетельство подключенного экрана.

## Промпты генерации

Общая часть для каждого изображения:

> Production game UI background texture for a dark gothic action RPG, vertical portrait 2:3 composition. Cold neutral GREY STONE and weathered dark iron, realistic painterly game environment art, elegant high-quality hand-painted material detail, subtle ash mist. Monochromatic graphite/slate/ivory palette, NO gold, NO red cloth, NO blue glow, NO bright colors. Purpose: background BEHIND a dense readable interface; bottom two thirds are deep charcoal grey with faint texture, visual storytelling concentrated in the TOP THIRD and far SIDE EDGES. Very low contrast behind the center where text and buttons will be laid over it. Soft vignette. Full bleed artwork ONLY: absolutely no letters, numbers, labels, symbols that look like text, borders, frames, buttons, sockets, people or UI overlays. Do not make a finished UI screen.

Дополнение `activities`:

> A foreboding gothic vestibule carved in grey granite, five deep arched doorways receding into darkness, an iron portcullis and old stone stairs at the sides; a place from which adventurers select an expedition. Architecture visible at the top, details fading toward empty dark stone below.

Дополнение `ascent`:

> The interior of a vast ancient grey stone tower, looking steeply upward through stacked landings and broken spiral staircases toward a small distant pale opening. Strong feeling of ASCENT and five levels, restrained central vertical axis, stone ribs on side edges, top ceiling fades into mist. Leave lower two thirds calm and very dark for five large floor cards; no literal numbered levels or diagram.

Дополнение `inscriptions`:

> An occult workbench for modifying dungeon maps: grey stone ritual table, dark iron seal stamps, carved stone runes with no legible text, charcoal wax seals, several small closed ink bottles, worn tools. Three or four objects along TOP and SIDE margins, empty dark matte stone across central and lower area. Overhead slightly oblique still life, subtle ivory highlights, restrained mysterious mood, no potion health red/blue colors.
