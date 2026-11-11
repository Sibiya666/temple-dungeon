"""Validate the delivered PDF and prepare a visual review of every page."""
from pathlib import Path
import re, json, hashlib, shutil
from pypdf import PdfReader
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[4]
HERE=Path(__file__).resolve().parent
TMP=ROOT/'tmp/pdfs/artbook'
PDF=ROOT/'output/pdf/Dialoid-Artbook-2026-11-11.pdf'
data=json.loads((HERE/'inventory.json').read_text(encoding='utf-8-sig'))
build=json.loads((HERE/'build-report.json').read_text(encoding='utf-8-sig'))
reader=PdfReader(str(PDF))
assert len(reader.pages)==build['pages']
ids=set();empty=[];sizes=[];text_pages=[]
for i,p in enumerate(reader.pages,1):
    text=p.extract_text() or '';text_pages.append(text)
    ids.update(re.findall(r'\bA\d{4}\b',text))
    if len(text.strip())<8:empty.append(i)
    if tuple(map(float,p.mediabox[2:]))!=(1000.,700.):sizes.append(i)
expected={a['id'] for a in data['artworks']}
assert not (expected-ids),sorted(expected-ids)
assert not empty and not sizes
assert all(a['pages'] and all(1<=p<=len(reader.pages) for p in a['pages']) for a in data['artworks'])
assert not build['unreadable'] and not build['unplaced'] and not build['text_overflows']
links=[]
for a in reader.pages[2].get('/Annots',[]):
    obj=a.get_object()
    if obj.get('/Subtype')=='/Link':links.append(obj)
assert len(links)==10
pages=sorted((TMP/'final-pages').glob('page-*.png'))
assert len(pages)==len(reader.pages),(len(pages),len(reader.pages))
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',13)
for start in range(0,len(pages),20):
    batch=pages[start:start+20]
    sheet=Image.new('RGB',(1248,1220),'#182127');draw=ImageDraw.Draw(sheet)
    for j,p in enumerate(batch):
        with Image.open(p) as im:
            im=im.convert('RGB');im.thumbnail((300,210),Image.Resampling.LANCZOS)
            x=12+(j%4)*310;y=12+(j//4)*240
            sheet.paste(im,(x,y));draw.text((x,y+213),f'{start+j+1:03}',fill='#d8d5c6',font=font)
    sheet.save(TMP/'contact-sheets'/f'sheet-{start//20+1:02}.jpg',quality=88)
shutil.copy2(pages[0],HERE/'cover.png')
result={'pdf_pages':len(reader.pages),'unique_art_ids':len(expected),'missing_ids':sorted(expected-ids),
        'empty_pages':empty,'wrong_page_sizes':sizes,'clickable_contents_links':len(links),
        'rendered_pages':len(pages),'review_contact_sheets':(len(pages)+19)//20,
        'sha256':hashlib.sha256(PDF.read_bytes()).hexdigest(),'bytes':PDF.stat().st_size,
        'visual_review':'See ARTBOOK-REVIEW.md; automated checks do not replace visual inspection.'}
(HERE/'verification.json').write_bytes(json.dumps(result,ensure_ascii=False,indent=2).encode('utf-8'))
print(json.dumps(result,ensure_ascii=False))
