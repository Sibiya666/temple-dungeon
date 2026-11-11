"""Build the complete local Dialoid visual archive without changing source art."""
from pathlib import Path
from collections import defaultdict, Counter
import os, json, hashlib, io, math, re, csv
from PIL import Image, ImageOps, ImageDraw
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.utils import ImageReader

ROOT = Path(__file__).resolve().parents[4]
BASE = Path('C:/Users/sibiya666/Documents/project/dialoid')
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'output/pdf/Dialoid-Artbook-2026-11-11.pdf'
TMP = ROOT / 'tmp/pdfs/artbook'
TMP.mkdir(parents=True, exist_ok=True)
RASTER = {'.png', '.jpg', '.jpeg', '.webp', '.bmp', '.tga', '.gif'}
MEDIA = RASTER | {'.svg', '.mp4', '.webm', '.glb', '.gltf', '.blend', '.fbx', '.efkefc', '.efkproj'}
SKIP = {'.git', '.godot', 'node_modules', 'bin', 'obj', '__pycache__', '.venv', 'venv', 'addons', 'vendor', 'artbook'}
TARGETS = ['docs/art', 'game/Ui/Art', 'assets', 'spike-3d']

def walk(root, target):
    start = root / target
    if not start.exists(): return
    for directory, dirs, files in os.walk(start):
        rel = Path(directory).relative_to(root).as_posix()
        dirs[:] = sorted(d for d in dirs if d not in SKIP)
        if rel == 'spike-3d/stablegen-trial':
            dirs[:] = [d for d in dirs if d in ['output', 'ComfyUI']]
        elif rel == 'spike-3d/stablegen-trial/ComfyUI':
            dirs[:] = [d for d in dirs if d == 'output']
        elif rel == 'spike-3d/stablegen-trial/ComfyUI/output':
            dirs[:] = [d for d in dirs if d == 'dialoid-room-v1']
        for name in sorted(files):
            p = Path(directory) / name
            if p.suffix.lower() in MEDIA: yield p

def section(path):
    p = path.lower()
    if p.startswith('assets/'): return 'library'
    if p.startswith('spike-3d/'):
        if '/briefs/' in p or '/gen/' in p or re.search(r'/cb_\d+',p): return 'characters'
        if '/efk/' in p or Path(p).stem.startswith('efk'): return 'effects'
        return 'world'
    if '/concepts/' in p or '/ronin/' in p or '/kitsune/' in p: return 'characters'
    if '/cursor/' in p or '/cursors/' in p: return 'cursors'
    if '/ui-styleguide/' in p or '/styleguide-presentation/' in p: return 'ui'
    if '/tower' in p: return 'tower'
    if '/skills/' in p and p.startswith('docs/'): return 'skills'
    if '/icons/' in p or '/skill' in p or '/portraits/' in p: return 'icons'
    if '/windowpanels/' in p: return 'world'
    return 'archive'

SECTIONS = {
 'world': ('Пространство башни', 'Архитектура, свет, материалы и технические пробы окружения.'),
 'characters': ('Персонажи и существа', 'Концепты, ортографические виды и исследования моделей.'),
 'tower': ('Башня и активности', 'Маршрут, события, расходники и визуальный язык активностей.'),
 'ui': ('Интерфейс в игре', 'Выбранный Tower05 и история развития окон. Кадр подтверждает только показанное состояние.'),
 'skills': ('Навыки и деревья', 'Структура деревьев и визуальные пробы. Названия и готовность сверяются с источником.'),
 'icons': ('Иконография', 'Навыки, узлы, восхождения, предметы и состояния. Оригиналы и масштабные пробы.'),
 'cursors': ('Курсоры', 'Стекло, металл и выбранные состояния указателя.'),
 'effects': ('Эффекты', 'Исходные изображения и технические листы эффектов.'),
 'archive': ('История поиска', 'Сравнения, старые варианты и материалы без подтверждённого статуса.'),
 'library': ('Библиотеки источников', 'Сторонние модели, текстуры и спрайты. Наличие не означает авторство или подтверждение прав.'),
}

def load_rules():
    p=HERE/'family-rules.json'
    if not p.exists(): return []
    data=json.loads(p.read_text(encoding='utf-8-sig'))
    if isinstance(data,dict): data=data.get('rules',[])
    return sorted(data,key=lambda r:len(r.get('prefix','')),reverse=True)

def provenance(rel, source, rules):
    for r in rules:
        if rel.startswith(r.get('prefix','!')) and r.get('source_root','any') in ['any',source]:
            return r.get('status','Статус не подтверждён'),r.get('provider',''),r.get('evidence','')
    if rel.startswith('assets/'): return 'Библиотека / внешние источники','','Лицензии и README исходной библиотеки'
    if rel.startswith('spike-3d/'): return 'Исследовательская / техническая проба','','README соответствующего прототипа'
    if '/root-after/' in rel: return 'Проверенный игровой кадр','','root-receipt.md в родительском наборе'
    if '/before/' in rel or '/source-before/' in rel: return 'Историческое состояние','','Снимок до изменений'
    return 'Архив / статус по первоисточнику','','Утверждение не предполагается по одному имени файла'

def inventory():
    rules=load_rules(); unique={}; records=[]; nonvisual=[]; failures=[]; raw_cache={}
    imports_path=HERE/'art-source-import.json'
    imported={r['path'] for r in json.loads(imports_path.read_text(encoding='utf-8-sig'))} if imports_path.exists() else set()
    for source,root in [('worktree',ROOT),('main-copy',BASE)]:
        for target in TARGETS:
            for p in walk(root,target):
                rel=p.relative_to(root).as_posix()
                rec={'source_root':source,'path':rel,'bytes':p.stat().st_size}
                if p.suffix.lower() not in RASTER:
                    nonvisual.append(rec); continue
                raw=hashlib.sha256(p.read_bytes()).hexdigest();rec['sha256']=raw
                if raw in raw_cache:
                    art=raw_cache[raw]
                    rec.update(width=art['width'],height=art['height'],pixel_sha256=art['pixel_sha256'],art_id=art['id'])
                    art['aliases'].append({'source_root':source,'path':rel,'sha256':raw});records.append(rec);continue
                try:
                    with Image.open(p) as im:
                        im=ImageOps.exif_transpose(im).convert('RGBA')
                        rec['width'],rec['height']=im.size
                        pix=hashlib.sha256(str(im.size).encode()+im.tobytes()).hexdigest()
                        rec['pixel_sha256']=pix
                except Exception as e:
                    failures.append({**rec,'error':str(e)}); continue
                key=pix
                if key not in unique:
                    uid=f'A{len(unique)+1:04}'
                    status,provider,evidence=provenance(rel,'main-copy' if source=='worktree' and rel in imported else source,rules)
                    unique[key]={'id':uid,'source_root':source,'path':rel,'absolute':str(p),'width':rec['width'],'height':rec['height'],
                                 'sha256':raw,'pixel_sha256':pix,'section':section(rel),'status':status,'provider':provider,'evidence':evidence,'aliases':[],'pages':[]}
                art=unique[key];raw_cache[raw]=art; rec['art_id']=art['id']; art['aliases'].append({'source_root':source,'path':rel,'sha256':raw});records.append(rec)
    items=list(unique.values())
    result={'edition':'2026-11-11','roots':{'worktree':str(ROOT),'main-copy':str(BASE)},'scan_targets':TARGETS,
            'excluded_directories':sorted(SKIP),'stablegen_allowlist':['output','ComfyUI/output/dialoid-room-v1'],
            'sources':records,'artworks':items,'nonvisual_media':nonvisual,'unreadable':failures}
    (HERE/'inventory.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8',newline='\n')
    print(f'INVENTORY sources={len(records)} unique={len(items)} nonvisual={len(nonvisual)} unreadable={len(failures)}',flush=True)
    return result

W,H=1000,700
BG='#0c1114'; FG='#d8d5c6'; MUTED='#82969c'; LINE='#35454b'; SURFACE='#121b20'
pdfmetrics.registerFont(TTFont('Body','C:/Windows/Fonts/segoeui.ttf'))
pdfmetrics.registerFont(TTFont('BodyBold','C:/Windows/Fonts/segoeuib.ttf'))
pdfmetrics.registerFont(TTFont('Display','C:/Windows/Fonts/georgia.ttf'))
text_overflows=[]

def wrap(text,font,size,width,maxlines=99):
    lines=[];line=''
    for word in str(text).split():
        # Split even a very long ID or source path into measured chunks.
        parts=[''];
        for ch in word:
            if pdfmetrics.stringWidth(parts[-1]+ch,font,size)>width: parts.append('')
            parts[-1]+=ch
        for part in parts:
            cand=(line+' '+part).strip()
            if pdfmetrics.stringWidth(cand,font,size)<=width: line=cand
            else: lines.append(line);line=part
    if line: lines.append(line)
    if len(lines)>maxlines:
        # Paths have full entries in the manifest; compact labels can be shortened.
        lines=lines[:maxlines]
        while pdfmetrics.stringWidth(lines[-1]+'…',font,size)>width: lines[-1]=lines[-1][:-1]
        lines[-1]+='…'
    return lines

class Book:
    def __init__(self, data):
        self.data=data; self.c=canvas.Canvas(str(OUT),pagesize=(W,H),pageCompression=1)
        self.c.setTitle('DIALOID | Полный артбук | 11.11.2026')
        self.c.setAuthor('Dialoid / визуальный архив проекта')
        self.page=0;self.entries=[];self.contact_pages=[];self.chapter_starts={}
    def begin(self,tag='ВИЗУАЛЬНЫЙ АРХИВ'):
        self.page+=1;c=self.c;c.setFillColor(BG);c.rect(0,0,W,H,fill=1,stroke=0)
        c.setStrokeColor(LINE);c.setLineWidth(.6);c.line(32,48,W-32,48)
        self.text(32,30,'DIALOID',8,'Body',MUTED);self.text(110,30,tag.upper(),7,'Body',MUTED)
        self.text(W-65,30,f'{self.page:03}',9,'Body',FG)
    def text(self,x,y,s,size=12,font='Body',color=FG):
        self.c.setFillColor(color);self.c.setFont(font,size);self.c.drawString(x,y,str(s))
        tw=pdfmetrics.stringWidth(str(s),font,size)
        if x+tw>W-20 or y<12 or y+size>H+1: text_overflows.append({'page':self.page,'text':str(s),'x':x,'y':y,'width':tw})
    def paragraph(self,x,y,s,width,size=11,leading=16,font='Body',color=MUTED,maxlines=99):
        for line in wrap(s,font,size,width,maxlines): self.text(x,y,line,size,font,color);y-=leading
        return y
    def image(self,art,box,limit=1600):
        x,y,w,h=box
        with Image.open(art['absolute']) as raw:
            im=ImageOps.exif_transpose(raw).convert('RGBA')
            im.thumbnail((limit,limit),Image.Resampling.LANCZOS)
            bg=Image.new('RGBA',im.size,(12,17,20,255));bg.alpha_composite(im);im=bg.convert('RGB')
            buf=io.BytesIO();im.save(buf,'JPEG',quality=87 if limit>=1600 else 73,subsampling=0 if limit>=1600 else 2);buf.seek(0)
            scale=min(w/im.width,h/im.height);iw,ih=im.width*scale,im.height*scale
            self.c.drawImage(ImageReader(buf),x+(w-iw)/2,y+(h-ih)/2,iw,ih,mask='auto')
        if self.page not in art['pages']:art['pages'].append(self.page)
    def end(self): self.c.showPage()
    def feature(self,title,art,caption=None):
        self.begin('Избранное / '+SECTIONS[art['section']][0]);self.text(32,651,title,27,'Display')
        self.image(art,(32,124,936,495),1800)
        self.text(32,102,art['id']+'  /  '+str(caption or art['status']),9,'Body',MUTED)
        self.paragraph(32,85,art['path'],936,7.3,10,maxlines=2)
        self.end()
    def cover(self,hero):
        self.begin('Полное собрание / 11.11.2026')
        if hero:self.image(hero,(390,70,578,598),1800)
        self.text(40,594,'DIALOID',64,'Display');self.text(44,550,'АРТБУК',22,'Body')
        self.paragraph(44,492,'Башенный мир. Персонажи. Интерфейс. Иконография.',295,15,23,color=FG)
        self.paragraph(44,194,'Полное собрание доступных материалов проекта. Избранные работы и каталог уникальных изображений с источниками.',280,11,17)
        self.text(44,91,'Срез 11 ноября 2026',10,'Body',MUTED);self.end()
    def intro(self):
        self.begin('Об издании');self.text(40,640,'Мир выше человека',35,'Display')
        self.paragraph(42,584,'Древний вертикальный мир раскрывается через высоту, глубину и свет. Человек невелик внутри башни. Холодный рассеянный свет выделяет формы; глубокая тень сохраняет неизвестность пространства.',520,16,25,color=FG)
        self.paragraph(42,441,'Железо, ткань, кожа, стекло и камень различаются материалом. Угольный и холодный серый удерживают фон; старая слоновая кость и серебро дают спокойные акценты. Витраж входит в этот язык как предмет и источник света.',520,12,19)
        self.paragraph(42,310,'Книга собрана из текущей UI-ветки и основной рабочей копии. Их история расходится: наличие изображения в архиве не означает его подключение в текущем runtime. Утверждение указывается только по документированному решению или ревью.',520,11,17)
        stats=[('Исходных raster-файлов',len(self.data['sources'])),('Уникальных изображений',len(self.data['artworks'])),('Прочих медиа/3D файлов',len(self.data['nonvisual_media']))]
        for i,(label,value) in enumerate(stats):
            y=562-i*108;self.text(665,y,str(value),39,'Display');self.text(665,y-27,label,10,'Body',MUTED)
        self.paragraph(42,151,'Как читать: крупные развороты показывают ключевые работы; далее идут все уникальные изображения по разделам. Код A0000 связывает подпись с inventory.json. Точные копии сведены в одну запись, все пути сохранены. Уменьшения и переработки остаются отдельными изображениями.',875,10,16)
        self.text(42,75,'Источник направления: docs/art/ART_DIRECTION.md; дополнительные статусы — README и manifest семейств.',8,'Body',MUTED)
        self.end()
    def contents(self,starts=None):
        self.begin('Навигация');self.text(40,640,'Содержание',35,'Display')
        self.text(43,589,'ИЗБРАННОЕ',9,'Body',MUTED);self.text(43,558,'Свет, силуэты и работающий интерфейс',19,'Display')
        if starts:self.text(910,558,str(starts['features']),15,'Body')
        y=508
        for key,(title,desc) in SECTIONS.items():
            self.text(44,y,title,17,'Display')
            if starts:
                self.text(910,y,f"{starts[key]:03}",13,'Body',MUTED)
                self.c.linkRect('',key,(40,y-7,960,y+23),relative=0,thickness=0)
            y-=40
        self.text(44,75,'Полный реестр источников и соответствий: inventory.json / inventory.csv рядом с книгой.',9,'Body',MUTED)
        self.end()
    def chapter(self,key):
        title,desc=SECTIONS[key];self.begin(title);self.chapter_starts[key]=self.page
        self.c.bookmarkPage(key);self.c.addOutlineEntry(title,key,level=0)
        self.text(42,607,title,37,'Display');self.paragraph(44,559,desc,730,14,22)
        self.text(44,410,'ПОЛНЫЙ КАТАЛОГ',9,'Body',MUTED)
        count=sum(1 for a in self.data['artworks'] if a['section']==key)
        self.text(40,305,str(count),95,'Display')
        self.paragraph(44,249,'уникальных изображений. Повторяющиеся копии имеют одну запись; исходные пути и хеши сохранены в реестре.',590,12,19)
        self.paragraph(44,127,'Утверждённый оригинал, игровая проверка и исследовательская проба — разные статусы. Подпись не меняет статус первоисточника и не заменяет проверку лицензии стороннего материала.',820,10,16)
        self.end()
    def contact(self,key,items):
        self.begin(SECTIONS[key][0]);self.text(32,656,SECTIONS[key][0],22,'Display')
        # Six landscape frames or twelve icons per page. Original proportions always preserved.
        landscape=sum(a['width']/a['height']>1.4 for a in items)>len(items)/2
        cols,rows=(2,3) if landscape else (4,3)
        gap=14;cw=(936-gap*(cols-1))/cols;ch=183
        for i,art in enumerate(items):
            col=i%cols;row=i//cols;x=32+col*(cw+gap);top=618-row*ch
            self.c.setFillColor(SURFACE);self.c.rect(x,top-126,cw,126,stroke=0,fill=1)
            self.image(art,(x+3,top-123,cw-6,120),640 if landscape else 288)
            label=Path(art['path']).stem
            self.paragraph(x,top-141,f"{art['id']}  {label}",cw,8.3,10.2,font='BodyBold',color=FG,maxlines=2)
            self.paragraph(x,top-165,art['status'],cw,7.1,9,color=MUTED,maxlines=2)
        self.contact_pages.append(self.page);self.end()
    def closing(self):
        self.begin('Реестр и происхождение');self.text(42,636,'За изображением — источник',32,'Display')
        y=566
        for title,text in [
            ('Авторский и проектный арт','Venice используется для концептов и задников; GPT — для иконок по выбранным референсам. Точное происхождение читается из соседних prompt/JSON/provenance файлов. Книга не объявляет права на генерации подтверждёнными.'),
            ('Состояние веток','Новая общая ветка объединяет Tower05, семь окон, Character и Skills с принятыми арт-семействами из device-handoff. Старые кадры сохраняются как история. Статус локальной реализации не означает поставку в main.'),
            ('Что не повторяется в книге','Кеши движка, импорты, двоичные сборки и примеры зависимостей не входят в визуальный каталог. Для StableGen включены только выходы проекта. Точные pixel-копии дедуплицированы; их исходные пути остаются в реестре.'),
            ('Векторы, 3D, видео и эффекты','SVG, GLB/FBX/Blender, проекты эффектов и видео записаны в nonvisual_media. Книга показывает существующие изображения моделей и кадров; она не выдаёт их за полный просмотр анимации или интерактивной сцены.'),
        ]:
            self.text(44,y,title,17,'Display');y=self.paragraph(44,y-29,text,885,11,17)-40
        self.text(44,74,'Полный машиночитаемый реестр: inventory.json. Табличный указатель: inventory.csv.',9,'Body',MUTED);self.end()

FEATURES=[
 ('Выбранный язык света','docs/art/icons/2026-10-31-venice-ui-concepts/d4.png'),
 ('Башня: пространство персонажа','docs/art/icons/2026-10-31-venice-tower-backgrounds/character.png'),
 ('Семейство пространств','docs/art/icons/2026-10-31-venice-full-backgrounds/comparison.png'),
 ('Материалы и свет','spike-3d/room-textures-v1/reports/artguide-light-high.png'),
 ('Кицунэ: исходный образ','docs/art/concepts/2026-10-31-rogue-kitsune/01-base.png'),
 ('Витраж: выбранный материал','docs/art/cursor/2026-11-03-venice-358/glass.png'),
 ('Первая витражная пятёрка','docs/art/icons/2026-11-03-stained-glass-skills/preview.png'),
 ('Иконки: исходный язык','docs/art/icons/2026-10-31-venice-tower-icons/comparison.png'),
 ('Башня: интерактивный маршрут','docs/art/ui-styleguide/2026-11-10-tower-montage-godot/parity-correction/root-after/reference-900-empty.png'),
 ('Персонаж: снаряжение и показатели','docs/art/ui-styleguide/2026-11-11-character-skills-runtime/root-after/charskills-900-character-expanded.png'),
 ('Навыки: предметное дерево','docs/art/ui-styleguide/2026-11-11-character-skills-runtime/root-after/charskills-900-skills-expanded.png'),
 ('Навыки: управление геймпадом','docs/art/ui-styleguide/2026-11-11-character-skills-runtime/root-after/charskills-720-skills-pad.png'),
]

def build(data):
    items=data['artworks'];lookup={}
    for a in items:
        for alias in a['aliases']:lookup.setdefault(alias['path'],a)
    features=[(t,lookup[p]) for t,p in FEATURES if p in lookup]
    # Add representative current runtime wide views for all remaining UI windows.
    used={a['id'] for _,a in features}
    for token,title in [('ascendancy','Восхождение'),('builds','Билды'),('breakdown','Разбор'),('simulation','Симуляция'),('log','Журнал боя'),('meter','Измеритель'),('charts','Графики')]:
        choices=[a for a in items if a['path'].endswith('/2026-11-10-seven-windows-runtime/root-after/seven-900-'+token+'.png')]
        if choices:
            a=choices[-1]
            if a['id'] not in used:features.append((title,a));used.add(a['id'])
    # Compute chapter starts before drawing the clickable contents.
    groups={k:sorted([a for a in items if a['section']==k],key=lambda a:(str(Path(a['path']).parent),a['path'])) for k in SECTIONS}
    chunks={}
    for key,arts in groups.items():
        chunks[key]=[];i=0
        while i<len(arts):
            lands=arts[i]['width']/arts[i]['height']>1.4
            n=6 if lands else 12
            # Keep similarly shaped rows together, avoiding lost cells or overflow.
            batch=[]
            while i<len(arts) and len(batch)<n:
                a=arts[i]
                if batch and (a['width']/a['height']>1.4)!=lands:break
                batch.append(a);i+=1
            chunks[key].append(batch)
    starts={'features':4};page=4+len(features)
    for k in SECTIONS:starts[k]=page;page+=1+len(chunks[k])
    book=Book(data);hero=lookup.get('docs/art/icons/2026-10-31-venice-ui-concepts/d4.png')
    book.cover(hero);book.intro();book.contents(starts)
    book.c.bookmarkPage('features');book.c.addOutlineEntry('Избранные работы','features',level=0)
    for title,art in features:book.feature(title,art)
    for key in SECTIONS:
        book.chapter(key)
        for batch in chunks[key]:book.contact(key,batch)
    book.closing();book.c.save()
    # Record page locations only after PDF is complete.
    (HERE/'inventory.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8',newline='\n')
    with (HERE/'inventory.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.writer(f,lineterminator="\n");writer.writerow(['id','section','status','source_root','path','width','height','pages','sha256','aliases'])
        for a in items:writer.writerow([a['id'],a['section'],a['status'],a['source_root'],a['path'],a['width'],a['height'],','.join(map(str,a['pages'])),a['sha256'],len(a['aliases'])])
    receipt={'pages':book.page,'images':len(items),'source_files':len(data['sources']),'unreadable':data['unreadable'],
             'unplaced':[a['id'] for a in items if not a['pages']],'text_overflows':text_overflows,'chapters':book.chapter_starts,
             'feature_count':len(features),'pdf_bytes':OUT.stat().st_size,'pdf_sha256':hashlib.sha256(OUT.read_bytes()).hexdigest()}
    (HERE/'build-report.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8',newline='\n')
    print(json.dumps(receipt,ensure_ascii=False),flush=True)
    assert not receipt['unplaced'] and not text_overflows,'Layout/completeness check failed'

if __name__=='__main__':
    OUT.parent.mkdir(parents=True,exist_ok=True)
    build(inventory())
