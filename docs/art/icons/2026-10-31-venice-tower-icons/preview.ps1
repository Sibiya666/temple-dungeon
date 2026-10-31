Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(1040,400)
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.Clear([System.Drawing.Color]::FromArgb(14,17,20))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [System.Drawing.Font]::new('Segoe UI',13)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(216,214,201))
$ids = @('cleave','steel_skin','fireball','resource')
$labels = @('Рассекающий удар','Стальная кожа','Огненный шар','Магический ресурс')
for($i=0;$i -lt 4;$i++) {
 $x = 20 + $i*260
 $img = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot ($ids[$i]+'.png')))
 $g.DrawString($labels[$i],$font,$brush,$x,14)
 $g.DrawImage($img,[System.Drawing.Rectangle]::new($x,48,220,220))
 $g.DrawImage($img,[System.Drawing.Rectangle]::new($x,286,64,64))
 $g.DrawImage($img,[System.Drawing.Rectangle]::new($x+88,302,32,32))
 $g.DrawImage($img,[System.Drawing.Rectangle]::new($x+144,306,24,24))
 $img.Dispose()
}
$g.DrawString('Venice · башенный мир · внизу: 64 / 32 / 24 px',$font,$brush,20,367)
$bitmap.Save((Join-Path $PSScriptRoot 'comparison.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bitmap.Dispose(); $font.Dispose(); $brush.Dispose()
