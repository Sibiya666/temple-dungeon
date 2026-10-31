"""Export generated masters unchanged in meaning to runtime and build review sheets."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

base = Path(__file__).resolve().parent
repo = base.parents[3]
parser = argparse.ArgumentParser()
parser.add_argument('--partial', action='store_true', help='Export completed images while generation continues')
args = parser.parse_args()
jobs = []
for family in ['warrior', 'apostate']:
    data = json.loads((base / f'{family}-briefs.json').read_text(encoding='utf-8-sig'))
    jobs += [dict(j, family=family) for j in data['icons']]
missing = []
completed = []
groups = {}
selections_path = base / 'selected-revisions.json'
selections = json.loads(selections_path.read_text(encoding='utf-8-sig')) if selections_path.exists() else {}
for job in jobs:
    relative = Path('skills') / f"{job['skill_id']}.png" if job['kind'] == 'skill' else Path(job['skill_id']) / f"{job['node_id']}.png"
    key = job['skill_id'] if job['kind'] == 'skill' else job['skill_id'] + '/' + job['node_id']
    relative = Path(selections.get(key, str(relative)))
    master = base / relative
    metadata = master.with_suffix('.json')
    if not master.exists() or not metadata.exists():
        missing.append(str(relative))
        continue
    with Image.open(master) as image:
        image.load()
        width, height = image.size
        if width != height or min(width, height) < 256:
            raise ValueError(f'Invalid master dimensions: {relative}: {image.size}')
        runtime = repo / job['runtime_path']
        runtime.parent.mkdir(parents=True, exist_ok=True)
        image.convert('RGB').resize((64, 64), Image.Resampling.LANCZOS).save(runtime)
    entry = dict(job, master=str(relative), metadata=str(relative.with_suffix('.json')),
                 master_size=[width, height], sha256=hashlib.sha256(master.read_bytes()).hexdigest(),
                 runtime_sha256=hashlib.sha256(runtime.read_bytes()).hexdigest())
    completed.append(entry)
    groups.setdefault('skills' if job['kind'] == 'skill' else job['skill_id'], []).append(entry)
manifest = {'provider': 'GPT built-in image_gen', 'expected': len(jobs), 'completed': len(completed),
            'missing': missing, 'reference': '../2026-10-31-venice-tower-icons/cleave.png', 'icons': completed}
fingerprints = {}
for entry in completed:
    fingerprint = entry['runtime_sha256']
    if fingerprint in fingerprints:
        raise ValueError(f"Duplicate runtime images: {fingerprints[fingerprint]} and {entry['runtime_path']}")
    fingerprints[fingerprint] = entry['runtime_path']
(base / 'manifest.json').write_bytes((json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
# Whitelist only finished assets. Existing Cleave paintings stay mapped throughout production.
source_skills = json.loads((repo / 'data/skills.json').read_text(encoding='utf-8-sig'))
dedicated = {'cleave': [n['id'] for s in source_skills if s['id'] == 'cleave' for n in s['tree']]}
for entry in completed:
    if entry['kind'] == 'node':
        dedicated.setdefault(entry['skill_id'], []).append(entry['node_id'])
lines = ['// Generated from completed swordsmen icon masters; see docs/art/icons/2026-11-01-swordsmen/export.py.',
         'namespace Dialoid.Game.Ui.Art;', '', 'public static partial class GameIcons', '{',
         '    private static readonly Dictionary<string, HashSet<string>> DedicatedNodeArt = new(StringComparer.Ordinal)', '    {']
for skill, node_ids in sorted(dedicated.items()):
    quoted = ', '.join(json.dumps(node) for node in node_ids)
    lines.append(f'        ["{skill}"] = new(StringComparer.Ordinal) {{ {quoted} }},')
lines += ['    };', '}', '']
(repo / 'game/Ui/Art/GameIcons.Swordsmen.Generated.cs').write_bytes('\n'.join(lines).encode('utf-8'))
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 16)
small = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 12)
reviews = base / 'review'
reviews.mkdir(exist_ok=True)
for group, entries in groups.items():
    sheet = Image.new('RGB', (960, ((len(entries) + 3) // 4) * 222), '#10171b')
    draw = ImageDraw.Draw(sheet)
    sizes = Image.new('RGB', (880, len(entries) * 78 + 28), '#10171b')
    sd = ImageDraw.Draw(sizes)
    sd.text((8, 4), '64 px                      32 px       24 px', font=small, fill='#aebdc0')
    for i, entry in enumerate(entries):
        with Image.open(base / entry['master']) as image:
            art = image.convert('RGB')
            x, y = (i % 4) * 240, (i // 4) * 222
            sheet.paste(art.resize((180, 180), Image.Resampling.LANCZOS), (x + 30, y + 3))
            draw.text((x + 7, y + 188), entry['name'], font=font, fill='#d0dbdc')
            draw.text((x + 7, y + 207), entry['node_id'] or entry['skill_id'], font=small, fill='#8fa3a9')
            sy = 28 + i * 78
            for size, sx in [(64, 12), (32, 110), (24, 175)]:
                sizes.paste(art.resize((size, size), Image.Resampling.LANCZOS), (sx, sy))
            sd.text((225, sy + 12), entry['name'], font=font, fill='#d0dbdc')
    sheet.save(reviews / f'{group}-contact.png')
    sizes.save(reviews / f'{group}-sizes.png')
print(json.dumps({'expected': len(jobs), 'completed': len(completed), 'missing': len(missing),
                  'by_family': {family: sum(e['family'] == family for e in completed) for family in ['warrior', 'apostate']}}, ensure_ascii=False))
if missing and not args.partial:
    raise SystemExit('Generation incomplete; see manifest.json for missing IDs')
