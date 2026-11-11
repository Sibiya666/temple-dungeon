$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path "$PSScriptRoot/../../../..").Path
$out=$PSScriptRoot
function C($hex){[Drawing.ColorTranslator]::FromHtml($hex)}
function Fill($x,$y,$w,$h,$hex='#10191D',$alpha=255){$c=C $hex;$b=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb($alpha,$c));$script:g.FillRectangle($b,[single]$x,[single]$y,[single]$w,[single]$h);$b.Dispose()}
function Line($x,$y,$x2,$y2,$hex='#2C3C42',$width=1){$p=[Drawing.Pen]::new((C $hex),$width);$script:g.DrawLine($p,[single]$x,[single]$y,[single]$x2,[single]$y2);$p.Dispose()}
function Txt($s,$x,$y,$size=20,$hex='#D8D5C6',$max=1400){$f=[Drawing.Font]::new($(if($size -ge 30){'Georgia'}else{'Segoe UI'}),$size,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel);$b=[Drawing.SolidBrush]::new((C $hex));$r=[Drawing.RectangleF]::new($x,$y,$max,($size*6));$script:g.DrawString($s,$f,$b,$r);$b.Dispose();$f.Dispose()}
function Outline($x,$y,$w,$h,$hex='#35464D',$width=1){Line $x $y ($x+$w) $y $hex $width;Line $x ($y+$h) ($x+$w) ($y+$h) $hex $width;Line $x $y $x ($y+$h) $hex $width;Line ($x+$w) $y ($x+$w) ($y+$h) $hex $width}
function Focus($x,$y,$w,$h){$a=12;foreach($xx in @($x,($x+$w))){$dx=if($xx -eq $x){$a}else{-$a};foreach($yy in @($y,($y+$h))){$dy=if($yy -eq $y){$a}else{-$a};Line $xx $yy ($xx+$dx) $yy '#D8D5C6' 3;Line $xx $yy $xx ($yy+$dy) '#D8D5C6' 3}}}
function Btn($s,$x,$y,$w=220,$active=$false,$focus=$false){Fill $x $y $w 46 '#10191D' 115;Outline $x $y $w 46 $(if($active){'#9BADB5'}else{'#35464D'});Txt $s ($x+16) ($y+9) 19;if($focus){Focus $x $y $w 46}}
function Row($label,$value,$x,$y,$w=590){Txt $label $x $y 21 '#A3B4BA' ($w-160);Txt $value ($x+$w-145) $y 22 '#D8D5C6' 145;Line $x ($y+38) ($x+$w) ($y+38)}
function Head($s,$x,$y,$w=600){Txt $s $x $y 18 '#A3B4BA' $w;Line $x ($y+31) ($x+$w) ($y+31)}
function Pic($path,$x,$y,$w,$h){$i=[Drawing.Image]::FromFile((Join-Path $root $path));$script:g.DrawImage($i,[single]$x,[single]$y,[single]$w,[single]$h);$i.Dispose()}
function Bar($label,$value,$pct,$x,$y,$w=650,$hex='#456674') {Fill $x $y $w 46 '#132027';Fill $x $y ($w*$pct) 46 $hex;Txt $label ($x+12) ($y+10) 19;Txt $value ($x+$w-240) ($y+10) 20 '#D8D5C6' 230}
function Begin($id,$title,$subtitle,$key,$index){
 $script:bmp=[Drawing.Bitmap]::new(1600,900);$script:g=[Drawing.Graphics]::FromImage($script:bmp);$g.SmoothingMode='AntiAlias';$g.TextRenderingHint='AntiAliasGridFit';$g.InterpolationMode='HighQualityBicubic'
 Fill 0 0 1600 900 '#080E11';Pic "docs/art/icons/2026-10-31-venice-full-backgrounds/$id.png" 0 -350 1600 1600;Fill 8 8 1584 884 '#0C1114' 175;Outline 8 8 1584 884 '#223038' 1
 Txt 'DIALOID' 32 24 30 '#A3B4BA';Txt '/' 190 30 18 '#526971';Txt $title 220 26 23;Btn '×   Закрыть' 1400 20 160
 Txt $subtitle 36 96 22 '#A3B4BA' 1220;Txt ("0$index / 07") 1420 99 19 '#A3B4BA'
 Line 32 149 1568 149 '#35464D';Line 32 836 1568 836 '#35464D';Txt "$key  Окно     Esc / B  Назад" 36 854 18 '#A3B4BA';Txt 'МАКЕТ · ДЕМОНСТРАЦИОННЫЕ ДАННЫЕ · НЕ RUNTIME' 1010 857 14 '#7A909A' 550
}
function Save($id){$bmp.Save((Join-Path $out "$id.png"),[Drawing.Imaging.ImageFormat]::Png);$g.Dispose();$bmp.Dispose()}

Begin 'ascendancy' 'Асцендант' 'Путь персонажа / Воитель · уровень 60' 'N' 1
Btn 'Берсерк   ▾' 36 176 380 $true;Txt 'Очки асценданта: 4 / 4' 450 187 23;Btn 'Вписать' 1230 176 160
Head 'ДЕРЕВО АСЦЕНДАНТА' 36 258 350;Head 'ВЫБРАННЫЙ УЗЕЛ' 1010 258 520
# Runtime topology: start in centre, six independent notables in a circle, no notable chains.
$cx=480;$cy=515;$ids=@('insatiable','blood_feast','reckless','whirl_rage','flesh_rend','battle_trance');$names=@('Неутолимый','Кровавый пир','Без оглядки','Вихрь ярости','Рассечение плоти','Боевой транс');$coords=@(@(480,300),@(704,407),@(704,623),@(480,730),@(256,623),@(256,407))
for($i=0;$i -lt 6;$i++){if($i -eq 0){Line $cx 465 $cx 414 '#617B86' 2;Line $cx 350 $cx 348 '#617B86' 2}elseif($i -eq 3){Line $cx 565 $cx 568 '#617B86' 2;Line $cx 632 $cx 682 '#617B86' 2}else{Line $cx $cy $coords[$i][0] $coords[$i][1] '#617B86' 2}}
function SocketIcon($id,$x,$y,$size=100,$selected=$false,$focused=$false){
 $cut=$size*.18;$pts=[Drawing.PointF[]]@([Drawing.PointF]::new($x+$cut,$y),[Drawing.PointF]::new($x+$size-$cut,$y),[Drawing.PointF]::new($x+$size,$y+$cut),[Drawing.PointF]::new($x+$size,$y+$size-$cut),[Drawing.PointF]::new($x+$size-$cut,$y+$size),[Drawing.PointF]::new($x+$cut,$y+$size),[Drawing.PointF]::new($x,$y+$size-$cut),[Drawing.PointF]::new($x,$y+$cut));$path=[Drawing.Drawing2D.GraphicsPath]::new();$path.AddPolygon($pts);$state=$g.Save();$g.SetClip($path);Pic "docs/art/ui-styleguide/2026-11-10-seven-windows/icons/originals/$id.png" $x $y $size $size;$g.Restore($state);$p=[Drawing.Pen]::new((C $(if($selected){'#B7CDD4'}else{'#536C77'})),2);$g.DrawPolygon($p,$pts);$p.Dispose();$path.Dispose();if($focused){Focus ($x-4) ($y-4) ($size+8) ($size+8)}
}
SocketIcon 'berserk_frenzy' ($cx-50) ($cy-50) 100

Txt 'Кровавое безумие' ($cx-103) ($cy+57) 20 '#D8D5C6' 240
Txt 'Старт · взят' ($cx-60) ($cy+88) 16 '#A3B4BA' 150
for($i=0;$i -lt 6;$i++){$x=$coords[$i][0];$y=$coords[$i][1];SocketIcon $ids[$i] ($x-48) ($y-48) 96 ($i -eq 1) ($i -eq 1);Txt $names[$i] ($x-104) ($y+55) 20 '#D8D5C6' 230;Txt $(if($i -lt 4){'Взят · 1 очко'}else{'Доступен · 1 очко'}) ($x-70) ($y+84) 15 '#A3B4BA' 200}
Line 968 258 968 810 '#35464D'
SocketIcon 'blood_feast' 1010 322 104 $true
Txt 'Кровавый пир' 1137 332 29 '#D8D5C6' 390
Txt 'Берсерк · notable · стоимость 1' 1137 378 17 '#A3B4BA' 390
Txt 'Максимум стаков кровотечения +7 (с 8 до 15). Каждый стак на основной цели дает +1% скорости атаки, максимум +15%.' 1010 475 23 '#D8D5C6' 500
Btn 'Снять узел' 1010 650 260
Txt 'A — взять / снять · LT / RT — масштаб' 1010 742 18 '#A3B4BA' 500
Txt 'Правый стик — сдвиг · R3 — вписать' 1010 780 18 '#A3B4BA' 500
Save 'ascendancy'

Begin 'builds' 'Билды' 'Библиотека эталонных билдов / загрузка и обмен кодом' 'B' 2
Fill 28 172 720 612 '#0C1114' 105
Head 'ЭТАЛОНЫ / DATA/BUILDS' 36 181 700;Head 'КОД ТЕКУЩЕГО БИЛДА' 810 181 720
Btn 'Основные (60 уровень)  ▾' 36 238 700
$buildNames=@('Воитель · Берсерк · Вихрь','Воитель · Громовержец · Сокрушение','Воитель · Огненный рассекающий удар','Чародей · Пиромант · Огненный шар','Чародей · Криомант · Ледяное копье','Чародей · Искра · гибрид')
for($i=0;$i -lt $buildNames.Count;$i++){ $y=312+$i*61;if($i -eq 0){Fill 36 $y 700 52 '#1A2C34';Outline 36 $y 700 52 '#9BADB5';Focus 36 $y 700 52};Txt $buildNames[$i] 54 ($y+12) 21;Line 36 ($y+53) 736 ($y+53)}
Btn 'Загрузить' 36 720 250 $true
Txt 'Код DLD1:… или JSON билда' 810 239 20 '#A3B4BA';Fill 810 286 720 268 '#091114';Outline 810 286 720 268;Txt 'DLD1:…' 831 308 23 '#A3B4BA';Txt 'Поле кода · содержимое не показано в макете' 831 503 17 '#7A909A'
Btn 'Экспорт' 810 588 180;Btn 'Импорт' 1010 588 180;Btn 'Копировать код' 810 658 300;Btn 'Вставить из буфера' 1130 658 400
Txt 'Загружен: Воитель · Берсерк · Вихрь' 810 747 20 '#9DB9BA'
Save 'builds'

Begin 'breakdown' 'Разбор' 'Раскрытие числа / источник, модификаторы и история' 'R' 3
Head 'ИСТОРИЯ / ПОСЛЕДНИЕ 10' 36 181 400;Head 'СНИМОК ВЫБРАННОГО ЧИСЛА' 490 181 1040
Btn 'DPS за сегмент' 36 241 400 $true $true;Txt 'Рассекающий удар · сегмент №3' 51 302 17 '#A3B4BA' 370
Btn 'Скорость атаки' 36 363 400;Btn 'Шанс критического удара' 36 428 400;Btn 'Максимум здоровья' 36 493 400
Txt 'Рассекающий удар' 490 240 25 '#A3B4BA';Txt 'DPS за сегмент' 490 285 32;Txt '4 800' 1240 273 58
Line 490 358 1530 358 '#536C77';Txt 'Производное число, разбора по модификаторам нет.' 490 390 23 '#D8D5C6' 1000
Txt 'Пояснение' 490 471 18 '#A3B4BA';Txt 'Сегмент №3: урон 144 000 за 30 с, с учетом защиты манекена.' 490 512 24 '#D8D5C6' 980
Txt 'Лист скилла относится к текущему билду и не учитывает защиту манекена. Запись сегмента остается снимком.' 490 615 21 '#A3B4BA' 1000
Txt 'Билд изменился после открытия: откройте число заново для свежего разбора.' 490 736 20 '#A9BAC1' 1000
Save 'breakdown'

Begin 'simulation' 'Симуляция' 'Проверка билда / параметры запуска и результат одной ревизии' 'M' 4
Head 'ПАРАМЕТРЫ' 36 181 430;Head 'РЕЗУЛЬТАТ / ТЕКУЩИЙ БИЛД' 520 181 1010
Txt 'Манекен' 36 242 18 '#A3B4BA';Btn 'Тренировочный  ▾' 36 277 430
Txt 'Режим' 36 343 18 '#A3B4BA';Btn 'Среднее  ▾' 36 379 430
Row 'Размер пака' '5' 36 455 430;Row 'Сид' '1' 36 517 430;Txt '□  Манекен бьет персонажа' 36 586 21
Btn 'Симулировать' 36 675 430 $true $true
Txt 'Среднее · пак 5 · сид 1' 520 239 20 '#A3B4BA';Txt 'DPS по одной цели' 520 294 21 '#A3B4BA';Txt '4 800' 520 330 54;Txt 'DPS по паку' 1050 294 21 '#A3B4BA';Txt '24 000' 1050 330 54
Head 'DPS ПО СКИЛЛАМ / ВСЕ ЦЕЛИ' 520 431 1010
Bar 'Рассекающий удар' '18 000' 0.75 520 484 1010;Bar 'Вихрь' '6 000' 0.25 520 545 1010 '#526171'
Row 'Урон попаданиями' '720 000' 520 631 1010;Row 'Урон по времени' '0' 520 687 1010;Btn 'Показать лог' 520 751 260
Txt 'При изменении билда: «билд изменился — пересчитайте»' 830 763 18 '#A3B4BA' 700
Save 'simulation'

Begin 'log' 'Журнал' 'События тренировочной комнаты / время, источник, результат' 'L' 5
Fill 28 252 1510 455 '#0C1114' 145
Btn 'Виды событий  ▾' 36 181 320;Btn 'Все скиллы  ▾' 380 181 400;Txt '☑  Автопрокрутка' 1110 193 21
Head 'ВРЕМЯ' 36 262 150;Head 'КТО' 220 262 330;Head 'ЧТО ПРОИЗОШЛО' 600 262 930
$entries=@(@('28,0 с','Рассекающий удар','Попадание · 2 400 физ. урона'),@('28,5 с','Боевой клич','Бафф применен'),@('29,0 с','Рассекающий удар','Критическое попадание · 4 800 физ. урона'),@('29,5 с','Рассекающий удар','Попадание · 2 400 физ. урона'),@('30,0 с','Комната','Сегмент №3 завершен'),@('35,0 с','Комната','DPS сегмента: 4 800 · к прошлому +8,2%'))
for($i=0;$i -lt $entries.Count;$i++){$y=322+$i*64;if($i -eq 2){Fill 36 ($y-6) 1494 53 '#172830';Outline 36 ($y-6) 1494 53 '#9BADB5';Focus 36 ($y-6) 1494 53};Txt $entries[$i][0] 48 $y 21 '#A3B4BA';Txt $entries[$i][1] 232 $y 21 '#D8D5C6' 350;Txt $entries[$i][2] 610 $y 21 '#D8D5C6' 890;Line 36 ($y+47) 1530 ($y+47)}
Txt 'Строк 6 из 42 · скрыты: тики недугов и зон' 36 759 20 '#A3B4BA';Txt 'RS — прокрутка · Y — к последней строке' 1030 759 18 '#A3B4BA'
Save 'log'

Begin 'meter' 'Измеритель' 'Сегмент боя / наблюдаемый урон с учетом защиты манекена' 'T' 6
Head 'ВЫБОР СЕГМЕНТА' 36 181 430;Btn 'Режим: DPS по скиллам  ▾' 520 181 510
Btn '№3 · 30,0 с · последний  ▾' 36 241 430 $true;Txt 'Сегмент закрыт' 36 313 21 '#A3B4BA';Row 'Длительность' '30,0 с' 36 366 430;Row 'Урон' '144 000' 36 428 430;Row 'DPS' '4 800' 36 490 430
Txt '+8,2% к прошлому сегменту' 36 563 22 '#9DB9BA';Btn 'Графики (G)' 36 642 430;Btn 'Сбросить…' 36 712 190
Txt 'Урон / длительность сегмента' 520 239 20 '#A3B4BA';Bar 'Рассекающий удар' '3 600' .75 520 306 1010;Outline 520 306 1010 46 '#9BADB5';Focus 520 306 1010 46;Bar 'Вихрь' '1 200' .25 520 371 1010 '#526171'
Head 'НАСТРОЙКИ КОМНАТЫ' 520 487 1010;Btn 'Случайность: Среднее  ▾' 520 543 590;Btn 'Сид: 1' 1140 543 390;Txt '☑  Числа урона' 520 625 21
Txt 'Нажмите на полосу, чтобы открыть разбор урона скилла.' 520 720 22 '#A3B4BA' 960
Txt 'Сегмент: первый удар → 5 с без ударов' 520 772 18 '#A3B4BA'
Save 'meter'
$script:bmp=[Drawing.Bitmap]::new((Join-Path $out 'meter.png'));$script:g=[Drawing.Graphics]::FromImage($bmp);$g.TextRenderingHint='AntiAliasGridFit';Fill 8 8 1584 884 '#080E11' 170;Fill 430 287 740 290 '#101B20' 240;Outline 430 287 740 290 '#9BADB5';Txt 'Сбросить измеритель?' 468 322 30;Txt 'Все сохраненные сегменты будут удалены. Журнал останется.' 468 386 22 '#A3B4BA' 660;Btn 'Да, сбросить' 468 482 310;Btn 'Отмена' 810 482 310 $true $true;Save 'meter-confirm'

Begin 'charts' 'Графики' 'Сегмент №3 · 30,0 с / выбор сегмента — в измерителе' 'G' 7
Fill 28 230 1510 292 '#0C1114' 175
Head 'УРОН ПО ВРЕМЕНИ / ЗА КАЖДУЮ СЕКУНДУ' 36 181 1494
for($i=0;$i -lt 4;$i++){$y=260+$i*61;Line 110 $y 1510 $y '#25363D';Txt ([string](6000-$i*2000)) 36 ($y-12) 16 '#A3B4BA'}
Line 110 260 110 443 '#536C77';$ys=@(422,365,380,316,338,286,310,270,317,290,332,285,316,267,300,322,279,312,269,300,285,318,260,300,280,327,298,315,278,320)
for($i=0;$i -lt ($ys.Count-1);$i++){Line (110+$i*48) $ys[$i] (110+($i+1)*48) $ys[$i+1] '#B3C4CA' 2;Line (110+$i*48) ($ys[$i]+35) (110+($i+1)*48) ($ys[$i+1]+35) '#648A9D' 2}
Txt '0 с' 110 451 17 '#A3B4BA';Txt '15 с' 786 451 17 '#A3B4BA';Txt '30 с' 1465 451 17 '#A3B4BA';Txt '—  Всего      —  Рассекающий удар' 110 489 18 '#A3B4BA'
Head 'DPS ПО СКИЛЛАМ' 36 553 710;Head 'УРОН ПО ТИПАМ' 820 553 710
Bar 'Рассекающий удар' '3 600' .75 36 609 710;Bar 'Вихрь' '1 200' .25 36 669 710 '#526171';Bar 'Физический' '144 000 · 100%' 1 820 609 710 '#526171'
Txt 'Ниже по прокрутке: потеря на защите по скиллам' 820 705 21 '#A3B4BA' 680;Txt 'дошло / броня / сопротивления / множитель / обнулено' 820 750 18 '#A3B4BA' 690
Fill 1562 181 4 620 '#25363D';Fill 1562 181 4 365 '#7B919A'
Save 'charts'

$ids=@('ascendancy','builds','breakdown','simulation','log','meter','charts')
$titles=@('01  Асцендант','02  Билды','03  Разбор','04  Симуляция','05  Журнал','06  Измеритель','07  Графики')
$script:bmp=[Drawing.Bitmap]::new(2400,2900);$script:g=[Drawing.Graphics]::FromImage($bmp);$g.InterpolationMode='HighQualityBicubic';$g.TextRenderingHint='AntiAliasGridFit';Fill 0 0 2400 2900 '#0C1114';Txt 'DIALOID / СЕМЬ ОКОН В СТИЛЕ БАШНИ' 50 32 38;Txt 'Варианты применения эталона · существующие Venice-задники · статические макеты' 50 88 22 '#A3B4BA'
for($i=0;$i -lt 7;$i++){$col=$i%2;$row=[math]::Floor($i/2);$x=50+$col*1170;$y=153+$row*680;$im=[Drawing.Image]::FromFile((Join-Path $out ($ids[$i]+'.png')));$g.DrawImage($im,[single]$x,[single]$y,1120,630);$im.Dispose();Txt $titles[$i] ($x+12) ($y+636) 24 '#D8D5C6'}
# Last card is a key rather than an eighth variant.
Txt 'Одна система — разные рабочие задачи' 1220 2240 32;Txt 'Угольная поверхность · холодный металл · слоновая кость' 1220 2310 23 '#A3B4BA' 1080;Txt 'Выбор: замкнутая линия. Фокус: отдельные угловые метки.' 1220 2375 23 '#A3B4BA' 1080;Txt 'Демонстрационные данные. Не скриншоты игры. Асцендант: 7 собственных GPT-иконок по Venice.' 1220 2460 23 '#A3B4BA' 1080
$bmp.Save((Join-Path $out 'overview.png'),[Drawing.Imaging.ImageFormat]::Png);$g.Dispose();$bmp.Dispose()
