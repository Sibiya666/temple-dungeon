$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$ids=@('berserk_frenzy','insatiable','blood_feast','reckless','whirl_rage','flesh_rend','battle_trance')
foreach($id in $ids){$im=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot "originals/$id.png"));foreach($size in @(64,32,24)){$b=[Drawing.Bitmap]::new($size,$size);$g=[Drawing.Graphics]::FromImage($b);$g.InterpolationMode='HighQualityBicubic';$g.CompositingQuality='HighQuality';$g.DrawImage($im,0,0,$size,$size);$b.Save((Join-Path $PSScriptRoot "export/$id-$size.png"),[Drawing.Imaging.ImageFormat]::Png);$g.Dispose();$b.Dispose()};$im.Dispose()}
$b=[Drawing.Bitmap]::new(1400,850);$g=[Drawing.Graphics]::FromImage($b);$g.Clear([Drawing.ColorTranslator]::FromHtml('#0C1114'));$g.TextRenderingHint='AntiAliasGridFit'
$font=[Drawing.Font]::new('Segoe UI',20,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel);$brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#D8D5C6'))
$g.DrawString('БЕРСЕРК / 7 СОБСТВЕННЫХ ИКОНОК / GPT ПО VENICE-ОРИГИНАЛУ',$font,$brush,24,18)
for($i=0;$i -lt 7;$i++){$y=68+$i*108;$g.DrawString($ids[$i],$font,$brush,24,$y+22);$x=310;foreach($size in @(64,32,24)){$im=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot "export/$($ids[$i])-$size.png"));$g.DrawImageUnscaled($im,$x,$y);$g.DrawString("$size px",$font,$brush,$x+76,$y+19);$im.Dispose();$x+=320}}
$b.Save((Join-Path $PSScriptRoot 'size-check.png'),[Drawing.Imaging.ImageFormat]::Png);$font.Dispose();$brush.Dispose();$g.Dispose();$b.Dispose()
