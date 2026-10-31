"""Build non-destructive review sheets from generated originals."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).parent
manifest = json.loads((root / 'manifest.json').read_text(encoding='utf-8'))
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 17)
sheet = Image.new('RGB', (1100, 5 * 260), '#10171b')
draw = ImageDraw.Draw(sheet)
small = Image.new('RGB', (850, 17 * 54 + 40), '#10171b')
sd = ImageDraw.Draw(small)
for i, entry in enumerate(manifest['icons']):
    art = Image.open(root / entry['file']).convert('RGB')
    x, y = (i % 4) * 275, (i // 4) * 260
    sheet.paste(art.resize((220, 220), Image.Resampling.LANCZOS), (x + 27, y + 5))
    draw.text((x + 8, y + 231), entry['name'], font=font, fill='#d2d7d9')
    sy = i * 54 + 30
    sd.text((10, sy + 8), entry['name'], font=font, fill='#d2d7d9')
    for size, sx in [(64, 300), (32, 410), (24, 490)]:
        # 64px previews use extra space horizontally; rows intentionally 54px,
        # so display a 48px sample here and keep exact sizes in separate sheets.
        if size != 64:
            small.paste(art.resize((size, size), Image.Resampling.LANCZOS), (sx, sy))
sheet.save(root / 'contact-sheet.png')
for size in [64, 32, 24]:
    sample = Image.new('RGB', (600, 17 * (size + 14) + 32), '#10171b')
    d = ImageDraw.Draw(sample)
    for i, entry in enumerate(manifest['icons']):
        y = 25 + i * (size + 14)
        art = Image.open(root / entry['file']).convert('RGB')
        sample.paste(art.resize((size, size), Image.Resampling.LANCZOS), (20, y))
        d.text((110, y + 5), entry['name'], font=font, fill='#d2d7d9')
    sample.save(root / f'review-{size}px.png')
print(f'Prepared contact sheet and 64/32/24px review sheets for {len(manifest["icons"])} icons')
