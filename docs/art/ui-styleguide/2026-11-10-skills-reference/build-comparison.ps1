$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$canvas = New-Object System.Drawing.Bitmap 908, 556
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#0C1114'))
$font = New-Object System.Drawing.Font 'Segoe UI', 16
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#D8D5C6'))
foreach ($entry in @(@('before', 20, 'До'), @('after', 464, 'После'))) {
    $source = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot ($entry[0] + '/reference-720-catalog.png')))
    $graphics.DrawString($entry[2], $font, $brush, [single]$entry[1], [single]12)
    # Original 422x476 window crop at 1:1. No repainting or alteration of the UI.
    $graphics.DrawImage($source, [System.Drawing.Rectangle]::new([int]$entry[1], 50, 422, 476), 850, 48, 422, 476, [System.Drawing.GraphicsUnit]::Pixel)
    $source.Dispose()
}
$canvas.Save((Join-Path $PSScriptRoot 'catalog-before-after.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$canvas.Dispose()
$font.Dispose()
$brush.Dispose()
