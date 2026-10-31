Add-Type -AssemblyName System.Drawing
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$manifest = Get-Content (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$masterRoot = Join-Path $repoRoot 'assets/icons/2026-10-31-tower-rollout/masters'
$backupRoot = Join-Path $repoRoot 'assets/icons/2026-10-31-tower-rollout/previous-runtime'
foreach ($entry in $manifest) {
 $relative = $entry.group + '/' + $entry.id + '.png'
 $runtime = Join-Path $repoRoot ('game/Ui/Art/Icons/' + $relative)
 $backup = Join-Path $backupRoot $relative
 New-Item -ItemType Directory -Force (Split-Path -Parent $backup) | Out-Null
 if ((Test-Path -LiteralPath $runtime) -and !(Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $runtime -Destination $backup }
 $source = Join-Path $masterRoot $relative
 if (!(Test-Path -LiteralPath $source)) { throw "Missing master: $relative" }
 $image = [System.Drawing.Image]::FromFile($source)
 $bitmap = [System.Drawing.Bitmap]::new(64,64)
 $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
 $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
 # Center crop is mechanical framing only; keep the generated light and color unchanged.
 $cropSide = [Math]::Min($image.Width,$image.Height)*0.85
 $cropRect = [System.Drawing.RectangleF]::new(($image.Width-$cropSide)/2,($image.Height-$cropSide)/2,$cropSide,$cropSide)
 $graphics.DrawImage($image,[System.Drawing.RectangleF]::new(0,0,64,64),$cropRect,[System.Drawing.GraphicsUnit]::Pixel)
 $bitmap.Save($runtime,[System.Drawing.Imaging.ImageFormat]::Png)
 $graphics.Dispose(); $bitmap.Dispose(); $image.Dispose()
}
$sheet = [System.Drawing.Bitmap]::new(1200,([int][Math]::Ceiling($manifest.Count/8)*170))
$draw = [System.Drawing.Graphics]::FromImage($sheet)
$draw.Clear([System.Drawing.Color]::FromArgb(13,16,19))
$draw.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [System.Drawing.Font]::new('Segoe UI',8)
for ($i=0; $i -lt $manifest.Count; $i++) {
 $entry = $manifest[$i]; $x=10+($i%8)*150; $y=10+[Math]::Floor($i/8)*170
 $img=[System.Drawing.Image]::FromFile((Join-Path $masterRoot ($entry.group+'/'+$entry.id+'.png')))
 $draw.DrawImage($img,[System.Drawing.Rectangle]::new($x,$y,96,96))
 $draw.DrawImage($img,[System.Drawing.Rectangle]::new($x+102,$y+16,32,32))
 $draw.DrawImage($img,[System.Drawing.Rectangle]::new($x+106,$y+60,24,24))
 $draw.DrawString(($entry.group+'/'+$entry.id).Replace('keystone_',''),$font,[System.Drawing.Brushes]::Ivory,[System.Drawing.RectangleF]::new($x,$y+102,140,52))
 $img.Dispose()
}
$sheet.Save((Join-Path $PSScriptRoot 'contact-sheet.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$draw.Dispose(); $sheet.Dispose(); $font.Dispose()
Write-Output ('EXPORTED '+$manifest.Count+' runtime icons at 64px')
