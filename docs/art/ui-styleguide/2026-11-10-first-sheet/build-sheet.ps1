Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path "$PSScriptRoot/../../../..").Path
$bmp=[Drawing.Bitmap]::new(2400,1600)
$g=[Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode='AntiAlias'; $g.TextRenderingHint='AntiAliasGridFit'; $g.InterpolationMode='HighQualityBicubic'
function C($v){[Drawing.ColorTranslator]::FromHtml($v)}
function Fill($x,$y,$w,$h,$color){$b=[Drawing.SolidBrush]::new((C $color));$g.FillRectangle($b,$x,$y,$w,$h);$b.Dispose()}
function Line($x,$y,$xx,$yy,$color='#353A42',$width=1){$p=[Drawing.Pen]::new((C $color),$width);$g.DrawLine($p,$x,$y,$xx,$yy);$p.Dispose()}
function Text($s,$x,$y,$size=22,$color='#D8D5C6',$serif=$false){$family=if($serif){'Georgia'}else{'Segoe UI'};$f=[Drawing.Font]::new($family,$size,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel);$b=[Drawing.SolidBrush]::new((C $color));$g.DrawString($s,$f,$b,$x,$y);$f.Dispose();$b.Dispose()}
function Pic($path,$x,$y,$w,$h){$i=[Drawing.Image]::FromFile((Join-Path $root $path));$g.DrawImage($i,$x,$y,$w,$h);$i.Dispose()}
function Frame($x,$y,$w,$h,$color='#68707B',$thick=2){Fill $x $y $w $h '#10191D'; Line $x $y ($x+$w) $y $color $thick;Line $x $y $x ($y+$h) $color $thick;Line $x ($y+$h) ($x+$w) ($y+$h) '#252B30' $thick;Line ($x+$w) $y ($x+$w) ($y+$h) '#252B30' $thick}
function Section($s,$x,$y,$w){Text $s $x $y 20 '#98ADB4';Line $x ($y+38) ($x+$w) ($y+38) '#29383E'}
function Icon($id,$x,$y,$size){Pic "docs/art/icons/2026-11-03-stained-glass-skills/masters/$id.png" $x $y $size $size}
Fill 0 0 2400 1600 '#0C1114'
Text 'DIALOID' 72 38 76 '#D8D5C6' $true
Text 'БАШЕННЫЙ МИР / UI STYLEGUIDE' 75 132 22 '#98ADB4'
Text '01' 2245 43 65 '#68707B' $true
Text 'Предложение • не внедрено' 1830 130 25 '#BCA36C'
Line 72 185 2328 185 '#43515A'
# hero window
Section '01 / ОКНО И КОМПОЗИЦИЯ' 72 216 1310
Frame 72 280 1310 640 '#68707B' 4
Pic 'docs/art/icons/2026-10-31-venice-full-backgrounds/ascendancy.png' 76 284 1302 632
# opaque quiet working area, architecture visible on peripheral 220px
Fill 320 318 1012 556 '#10191D'
Text 'Навыки' 360 340 46 '#D8D5C6' $true
Text 'Воитель / дерево навыка' 362 405 22 '#98ADB4'
Text '×' 1282 332 34 '#98ADB4'
Line 360 452 1290 452 '#35434B'
$ids=@('cleave','battle_cry','fireball','ice_lance','spark')
$labels=@('Удар','Клич','Огонь','Лёд','Искра')
for($i=0;$i -lt 5;$i++){ $x=360+$i*184;Icon $ids[$i] $x 486 144;Text $labels[$i] $x 645 22 '#D8D5C6';Line $x 486 ($x+144) 486 '#68707B';Line $x 486 $x 630 '#68707B'}
Text 'Рассекающий удар' 360 704 30 '#D8D5C6' $true
Text 'Крупная форма. Тихий металл. Свет внутри стекла.' 360 752 23 '#98ADB4'
Frame 1070 794 220 54 '#87969C';Text 'Назначить' 1118 805 24
Text 'Макет окна • существующий задник Venice' 72 937 20 '#98ADB4'
# right material/type
Section '02 / МАТЕРИАЛ И ЦВЕТ' 1440 216 888
$cols=@('#0C1114','#162126','#353A42','#68707B','#D8D5C6','#73352F','#7099A8')
for($i=0;$i -lt 7;$i++){Fill (1440+$i*126) 280 108 64 $cols[$i];Text $cols[$i] (1440+$i*126) 358 17 '#98ADB4'}
Text 'Холодная грань / графит / мягкая тень' 1440 410 27 '#D8D5C6' $true
Text 'Золото — только вариант выбора, не общая оправа.' 1440 458 22 '#98ADB4'
Section '03 / ТИПОГРАФИКА И УПРАВЛЕНИЕ' 1440 520 888
Text 'Заголовок окна' 1440 580 40 '#D8D5C6' $true
Text 'Основной текст · числа 1 280 / 1 600' 1440 636 26
Text 'Пояснение · требование · причина блокировки' 1440 682 22 '#98ADB4'
for($i=0;$i -lt 3;$i++){ $x=1440+$i*300; $co=@('#68707B','#A0B1B8','#353A42')[$i];Frame $x 752 260 58 $co;Text @('Применить','Наведение','Недоступно')[$i] ($x+27) 766 23 $(if($i -eq 2){'#68747A'}else{'#D8D5C6'}) }
Text 'Состояние не меняет размер и положение элемента.' 1440 848 22 '#98ADB4'
# bottom 3 modules
Section '04 / НОДА: СОСТОЯНИЯ ОПРАВЫ' 72 1004 810
for($i=0;$i -lt 4;$i++){ $x=78+$i*204;$y=1080;$s=150;$cut=18
$pts=[Drawing.PointF[]]@([Drawing.PointF]::new($x+$cut,$y),[Drawing.PointF]::new($x+$s-$cut,$y),[Drawing.PointF]::new($x+$s,$y+$cut),[Drawing.PointF]::new($x+$s,$y+$s-$cut),[Drawing.PointF]::new($x+$s-$cut,$y+$s),[Drawing.PointF]::new($x+$cut,$y+$s),[Drawing.PointF]::new($x,$y+$s-$cut),[Drawing.PointF]::new($x,$y+$cut))
$b=[Drawing.SolidBrush]::new((C '#162126'));$g.FillPolygon($b,$pts);$b.Dispose();Icon 'spark' ($x+20) ($y+20) 110
$co=@('#68707B','#A0B1B8','#BCA36C','#BCA36C')[$i];$p=[Drawing.Pen]::new((C $co),2);$g.DrawPolygon($p,$pts);$p.Dispose();Text '5' ($x+123) ($y+117) 24
if($i -eq 3){Line ($x-3) ($y+24) ($x+24) ($y-3) '#D8D5C6' 4;Line ($x+126) ($y+153) ($x+153) ($y+126) '#D8D5C6' 4}
Text @('Обычная','Наведение','Выбрана','Выбор + фокус')[$i] $x 1255 20 '#98ADB4'}
Text 'Иллюстрация не содержит рамок, ранга и фокуса.' 72 1314 22 '#98ADB4'
Text 'Силуэт ноды и золото — предложенный вариант.' 72 1352 21 '#98ADB4'
# literal export sizes, not upscaled
Text 'Контроль размера / 64 · 32 · 24 px' 72 1404 20 '#98ADB4'
$ix=72
foreach($sz in @(64,32,24)){Pic "docs/art/icons/2026-11-03-stained-glass-skills/export/cleave-$sz.png" $ix 1450 $sz $sz;$ix+=($sz+28)}
# waterfall demo
Section '05 / HUD: БАШЕННЫЕ ВОДОПАДЫ' 948 1004 610
for($i=0;$i -lt 2;$i++){ $x=980+$i*286;$y=1116;$hh=200;$pct=@(.75,.4)[$i];Frame ($x-18) ($y-32) 146 260 '#68707B';Fill $x $y 108 $hh '#080D10';Fill $x $y 108 ([int]($hh*$pct)) @('#73352F','#7099A8')[$i];for($j=1;$j -lt 5;$j++){Line ($x-7) ($y+$j*40) $x ($y+$j*40) '#98ADB4'};Line ($x-12) ($y-9) ($x+120) ($y-9) '#98ADB4' 3;Line ($x-12) ($y+214) ($x+120) ($y+214) '#68707B' 3;Text @('Здоровье','Ресурс')[$i] ($x-18) 1380 23;Text @('75 / 100','40 / 100')[$i] ($x-18) 1418 23}
Text 'Статическая схема • длина потока = доля' 948 1470 20 '#98ADB4'
# real screenshot
Section '06 / КОНТЕКСТ ИГРЫ' 1624 1004 704
Pic 'docs/art/skills/2026-10-31-art-guide-runtime/review/task-14-hud-fight.png' 1624 1070 704 440
Text 'Архивный игровой кадр · до унификации' 1624 1518 20 '#98ADB4'
Line 72 1558 2328 1558 '#29383E'
Text 'ОСНОВА: ART_DIRECTION / УТВЕРЖДЁННАЯ ВИТРАЖНАЯ ПЯТЁРКА / VENICE' 72 1570 17 '#98ADB4'
Text '10.11.2026  /  ЛИСТ 01' 2080 1570 17 '#98ADB4'
$bmp.Save((Join-Path $PSScriptRoot 'dialoid-ui-sheet-01.png'),[Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$bmp.Dispose()

