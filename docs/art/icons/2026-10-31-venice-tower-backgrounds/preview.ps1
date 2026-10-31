Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::new(1290,480)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(13,16,19))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [System.Drawing.Font]::new('Segoe UI',15)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(218,216,204))
$ids = @('character','skills','passives')
$labels = @('Персонаж','Навыки','Пассивки')
for($i=0;$i -lt 3;$i++) {
 $x = 15 + 430*$i
 $g.DrawString($labels[$i],$font,$brush,$x,10)
 $img = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot ($ids[$i]+'.png')))
 $g.DrawImage($img,[System.Drawing.Rectangle]::new($x,48,400,400))
 $img.Dispose()
}
$bmp.Save((Join-Path $PSScriptRoot 'comparison.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $font.Dispose(); $brush.Dispose()
