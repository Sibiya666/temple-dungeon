"""Production exports and size QA; never repaints generated artwork."""
from pathlib import Path
import json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
GAME = ROOT.parents[3] / 'game' / 'Ui' / 'Tower' / 'Art'
IDS = ['imbue','imbue_type','imbue_tower','clear','shuffle','reroll','upgrade','remove',
       'biased_rare_elite','biased_rare_reward','biased_epic_elite','biased_epic_reward','bias_value','tower','summit']

report = []
sheet = Image.new('RGB', (860, len(IDS)*100+45), '#0c1114')
draw = ImageDraw.Draw(sheet)
draw.text((10, 12), 'Dialoid Tower | ID / 128 / 64 / 32 / 24 / 16 | source alpha preserved', fill='#d8d5c6')
for row, icon_id in enumerate(IDS):
    source = ROOT / 'originals' / (icon_id+'.png')
    if not source.exists():
        continue
    im = Image.open(source).convert('RGBA')
    alpha = im.getchannel('A')
    bbox = alpha.point(lambda x: 255 if x > 8 else 0).getbbox()
    if bbox is None:
        raise ValueError(f'{icon_id}: empty alpha')
    # Tighten only excess transparent margin; preserve aspect and centered breathing space.
    crop = im.crop(bbox)
    side = max(crop.size)
    square = Image.new('RGBA', (side, side))
    square.alpha_composite(crop, ((side-crop.width)//2, (side-crop.height)//2))
    production = Image.new('RGBA', (128,128))
    scaled = square.resize((112,112), Image.Resampling.LANCZOS)
    production.alpha_composite(scaled,(8,8))
    destination = GAME / ('symbols' if icon_id in ['tower','summit'] else 'currencies') / (icon_id+'.png')
    destination.parent.mkdir(parents=True, exist_ok=True)
    production.save(destination)
    y = row*100+45
    draw.text((10,y+28), icon_id, fill='#d8d5c6')
    for x, size in [(230,80),(350,64),(455,32),(540,24),(610,16)]:
        export = production.resize((size,size), Image.Resampling.LANCZOS)
        # 80px display of 128px runtime asset, to keep full sheet reasonably sized.
        sheet.paste(export,(x,y+3),export)
        if size != 80:
            export.save(ROOT / 'previews' / f'{icon_id}-{size}.png')
        draw.text((x,y+84), '128 at 80' if size == 80 else str(size), fill='#9baeb3')
    report.append({'id':icon_id,'original':str(source.relative_to(ROOT)), 'source_size':list(im.size),
                   'source_alpha_extrema':list(alpha.getextrema()),'visible_bounds':list(bbox),
                   'runtime':str(destination),'runtime_size':[128,128], 'runtime_alpha_extrema':list(production.getchannel('A').getextrema())})
sheet.save(ROOT / 'previews' / 'contact-sheet.png')
(ROOT / 'metadata' / 'size-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Exported {len(report)}/{len(IDS)} semantic icons with preserved alpha')
