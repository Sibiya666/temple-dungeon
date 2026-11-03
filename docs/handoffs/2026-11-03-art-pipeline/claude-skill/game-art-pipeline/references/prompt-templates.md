# Шаблоны промптов и пояснения

Промпты ниже — заготовки, а не гарантированные заклинания. Заполни предмет, назначение и сохраняемые свойства. Передай сам PNG выбранного оригинала. Не добавляй автоматически все отрицания из чужого проекта.

## 1. Новый концепт в Venice

```text
Create one original [asset or environment] for [purpose].
Subject and function: [what it is and why the player sees it].
Composition and viewpoint: [specific framing, scale, occupied and quiet areas].
Materials: [distinguishable materials].
Lighting: [source, direction, softness, visible and shadowed planes].
Palette and detail: [restrained targets appropriate to the project].
If a reference is supplied, use it for [exact role].
Do not add [task-specific unwanted elements]. No copied logos or game assets.
```

Для поиска разных направлений меняй композицию, формы, пропорции и визуальный язык. Для сохранения выбранного персонажа, наоборот, зафиксируй его идентичность. Один шаблон не должен одновременно требовать полной новизны и неизменности всех предметов.

## 2. Самостоятельный задник в Venice

```text
The reference defines atmosphere and material rendering only, not a scene to preserve.
Create a new [room/function] with its own architecture, viewpoint and spatial staging.
Scene: [concrete composition, focal object placement, depth and light source].
Keep the [specified area] calm and low contrast for live UI content.
Preserve the reference's lighting language, palette and level of detail.
No UI, readable text, numbers, charts, controls or baked interaction states.
Do not repeat the reference's exact room and merely swap one object.
```

Реальный использованный промпт тронной шахты находится в `examples/backgrounds/ascendancy.txt`; её исходный художественный ориентир — `examples/reference/approved-ui.png`. В своём проекте проценты спокойной области задавай по настоящей геометрии панели.

## 3. Узкая предметная замена для GPT

```text
Edit the supplied image. Change ONLY [specific object] into [new object].
Keep [orientation, placement and scale that matter].
Preserve the original background, palette, exposure, contrast, lighting direction,
edge softness, material rendering and level of detail.
Do not reinterpret or enhance the art style. Do not add glow, rim light,
expressive brush strokes, extra texture, particles, ornament, text or a frame.
Everything outside [the changed object] stays unchanged.
```

Это применяем, когда нужна одна семья изображений. Для новой сцены такой промпт слишком строг. Не добавляй следом «сделай лучше», «предложи своё видение» или «усиль живописность»: они противоречат сохранению манеры.

## 4. Витражная иконка навыка

```text
Edit the supplied reference into ONE square ARPG skill icon.
Replace the central shard completely with [specific meaningful skill silhouette].
The symbol itself is assembled from a few broad pieces of smoky stained glass
joined by dimensional dark lead came: glass and metal form the silhouette,
not a painting of a symbol on the same generic shard.
Preserve the reference's glass depth, lead material, restrained transmitted light,
soft shadows, background tone, contrast and rendering.
Keep a strong readable silhouette with clear margins.
No general glow, extra stylization, white drawn outline, gold decoration,
stone-noise overlay, flat SVG look, full window, text, frame or UI states.
```

Меч и дуга должны различаться, рог не должен превращаться в общий знак звука, молния — в стеклянный прямоугольник с молнией, нарисованной сверху. У каждого узла собственный смысловой рисунок. Точные пять использованных запросов находятся в `examples/stained-glass/prompts/`.

## 5. Удаление фона

```text
Background extraction only. Remove the uniform backdrop and its cast shadow.
Produce a true transparent RGBA background.
Preserve the exact object, shape, orientation, colors, exposure, material,
shading, highlights and detail. Do not redraw, sharpen, relight or stylize it.
Remove background visible through real holes.
Keep [glass face / mesh / other real semitransparent material] as part of the object.
Do not replace transparency with a checkerboard or a black rectangle.
```

В инструменте также включи настоящий режим прозрачного фона, если он доступен. После результата проверь alpha и сравни цвет с оригиналом: текстовый запрет не гарантирует отсутствия дрейфа.

## 6. Запрос независимому куратору

```text
Read-only review. Compare the selected reference, all masters, 64/32/24 px exports
and actual game screenshots. Check meaning by exact asset ID, silhouette, material,
light, contrast, crop and separate UI states. Do not edit the author's files.
Distinguish blockers, known limitations and taste preferences.
State APPROVE or REVISE and the scope: comparison only or runtime adoption.
Do not declare runtime ready from a contact sheet alone.
```

## Что отправить генератору

Один запрос включает сам входной PNG, один промпт, цель и явный список сохраняемых свойств. Текстовый путь без прикреплённого/доступного изображения не является референсом. Для серии отправляем отдельный запрос на каждый мастер, каждый раз от оригинала. Реально использованную модель/инструмент и параметры записываем по результату, не придумываем.
