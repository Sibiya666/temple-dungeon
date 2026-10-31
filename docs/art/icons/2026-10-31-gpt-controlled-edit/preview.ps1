Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$bmp = [System.Drawing.Bitmap]::new(780,850)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(16,18,21))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [System.Drawing.Font]::new('Segoe UI',14)
$g.DrawString('Venice: оригинал',$font,[System.Drawing.Brushes]::Ivory,15,10)
$g.DrawString('GPT: только замена предмета',$font,[System.Drawing.Brushes]::Ivory,405,10)
$paths = @((Join-Path $root '2026-10-31-venice-tower-icons/cleave.png'),(Join-Path $PSScriptRoot 'icon.png'),(Join-Path $root '2026-10-31-venice-tower-backgrounds/skills.png'),(Join-Path $PSScriptRoot 'background.png'))
for($i=0;$i -lt 4;$i++) {
 $img = [System.Drawing.Image]::FromFile($paths[$i])
 $x = 15 + ($i%2)*390
 $y = 50 + [Math]::Floor($i/2)*395
 $g.DrawImage($img,[System.Drawing.Rectangle]::new($x,$y,360,360))
 $img.Dispose()
}
$bmp.Save((Join-Path $PSScriptRoot 'comparison.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $font.Dispose()
