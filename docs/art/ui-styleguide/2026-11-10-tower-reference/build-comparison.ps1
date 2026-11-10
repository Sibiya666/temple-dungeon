$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$canvas = New-Object System.Drawing.Bitmap 2590, 780
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.Clear([System.Drawing.ColorTranslator]::FromHtml('#0C1114'))
$font = New-Object System.Drawing.Font 'Segoe UI', 18
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#D8D5C6'))
foreach($entry in @(@('before',10,'До'),@('after',1300,'После'))) {
    $image=[System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot ($entry[0]+'/reference-720-selected-focus.png')))
    $g.DrawString($entry[2],$font,$brush,[single]$entry[1],[single]8)
    $g.DrawImageUnscaled($image,[int]$entry[1],50)
    $image.Dispose()
}
$canvas.Save((Join-Path $PSScriptRoot 'tower-before-after.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $canvas.Dispose(); $font.Dispose(); $brush.Dispose()