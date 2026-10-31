Add-Type -AssemblyName System.Drawing
$ids = @('ascendancy','builds','breakdown','simulation','log','meter','charts')
$labels = @('Асцендант','Билды','Разбор','Симуляция','Журнал','Измеритель','Графики')
$bmp = [System.Drawing.Bitmap]::new(1200,650)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(13,16,19))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [System.Drawing.Font]::new('Segoe UI',13)
for($i=0;$i -lt $ids.Count;$i++) {
 $x=15+($i%4)*300; $y=10+[Math]::Floor($i/4)*325
 $g.DrawString($labels[$i],$font,[System.Drawing.Brushes]::Ivory,$x,$y)
 $img=[System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot ($ids[$i]+'.png')))
 $g.DrawImage($img,[System.Drawing.Rectangle]::new($x,$y+30,275,275)); $img.Dispose()
}
$bmp.Save((Join-Path $PSScriptRoot 'comparison.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $font.Dispose()
