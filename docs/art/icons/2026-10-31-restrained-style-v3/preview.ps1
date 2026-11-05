Add-Type -AssemblyName System.Drawing
$outDir = $PSScriptRoot
$repoDir = (Resolve-Path (Join-Path $outDir '../../../..')).Path
$canvas = [System.Drawing.Bitmap]::new(1280, 840)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.Clear([System.Drawing.Color]::FromArgb(15,17,22))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [System.Drawing.Font]::new('Segoe UI',16)
$small = [System.Drawing.Font]::new('Segoe UI',12)
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(225,224,214))
$muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(166,171,179))
$shade = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(120,5,8,12))
$g.DrawString('Проба стиля: задники и иконка — макет, не игровой скриншот',$font,$white,20,12)
$cases = @(
 @('Персонаж · текущий', (Join-Path $repoDir 'game/Ui/Art/WindowPanels/character-v1.png')),
 @('Персонаж · новый', (Join-Path $outDir 'character.png')),
 @('Навыки · текущий', (Join-Path $repoDir 'game/Ui/Art/WindowPanels/skills-v1.png')),
 @('Навыки · новый', (Join-Path $outDir 'skills.png'))
)
$icon = [System.Drawing.Image]::FromFile((Join-Path $outDir 'cleave.png'))
for ($i=0; $i -lt $cases.Count; $i++) {
 $x = 20 + $i * 315
 $g.DrawString($cases[$i][0],$small,$white,$x,50)
 $bg = [System.Drawing.Image]::FromFile($cases[$i][1])
 $g.DrawImage($bg,[System.Drawing.Rectangle]::new($x,80,295,690))
 $bg.Dispose()
 $g.FillRectangle($shade,$x,370,295,400)
 $g.DrawString('Рассекающий удар',$font,$white,$x+16,392)
 $g.DrawImage($icon,[System.Drawing.Rectangle]::new($x+16,440,64,64))
 $g.DrawString('Иконка 64 px',$small,$white,$x+96,448)
 $g.DrawString('Основной навык',$small,$muted,$x+96,475)
 $g.DrawImage($icon,[System.Drawing.Rectangle]::new($x+16,535,32,32))
 $g.DrawString('Иконка 32 px',$small,$white,$x+64,539)
 $g.DrawString('Пример вторичного текста',$small,$muted,$x+16,605)
 $g.DrawString('Спокойный фон под числа',$small,$white,$x+16,648)
}
$g.DrawString('Одинаковые текст, затемнение и новая иконка на всех четырёх панелях.',$small,$muted,20,792)
$canvas.Save((Join-Path $outDir 'comparison.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$icon.Dispose(); $g.Dispose(); $canvas.Dispose(); $font.Dispose(); $small.Dispose(); $white.Dispose(); $muted.Dispose(); $shade.Dispose()
