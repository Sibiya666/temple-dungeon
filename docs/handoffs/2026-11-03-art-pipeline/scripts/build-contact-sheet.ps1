#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$InputDir = (Join-Path $PSScriptRoot '../examples/stained-glass/masters'),
    [string]$OutDir = (Join-Path $PSScriptRoot '../outputs/contact-sheet')
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This offline script requires PowerShell 7 on Windows (System.Drawing).' }
Add-Type -AssemblyName System.Drawing

$inputPath = (Resolve-Path -LiteralPath $InputDir).Path
if (-not (Test-Path -LiteralPath $inputPath -PathType Container)) { throw 'InputDir must be a directory.' }
$outputPath = [IO.Path]::GetFullPath($OutDir)
function Assert-OutputOutsideMasters([string]$ResolvedOutput) {
    $inputPrefix = $inputPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($ResolvedOutput.Equals($inputPath, [StringComparison]::OrdinalIgnoreCase) -or
        $ResolvedOutput.StartsWith($inputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutDir must be outside InputDir; masters are never overwritten.'
    }
}
Assert-OutputOutsideMasters $outputPath
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$outputPath = (Resolve-Path -LiteralPath $outputPath).Path
Assert-OutputOutsideMasters $outputPath
$images = @(Get-ChildItem -LiteralPath $inputPath -Filter '*.png' -File | Sort-Object Name)
if ($images.Count -eq 0) { throw 'InputDir has no PNG masters.' }
$exportPath = Join-Path $outputPath 'export'
New-Item -ItemType Directory -Force -Path $exportPath | Out-Null

function Fit-Rect([System.Drawing.Image]$Image, [float]$X, [float]$Y, [float]$Size) {
    $factor = $Size / [Math]::Max($Image.Width, $Image.Height)
    $width = $Image.Width * $factor
    $height = $Image.Height * $factor
    return [System.Drawing.RectangleF]::new($X + ($Size - $width) / 2, $Y + ($Size - $height) / 2, $width, $height)
}

$columns = [Math]::Min(5, $images.Count)
$rows = [int][Math]::Ceiling($images.Count / $columns)
$sheet = [System.Drawing.Bitmap]::new($columns * 250, $rows * 420 + 28)
$draw = [System.Drawing.Graphics]::FromImage($sheet)
$font = [System.Drawing.Font]::new('Segoe UI', 12)
$smallFont = [System.Drawing.Font]::new('Segoe UI', 10)
$manifest = @()
try {
    $draw.Clear([System.Drawing.Color]::FromArgb(13, 17, 20))
    $draw.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    for ($i = 0; $i -lt $images.Count; $i++) {
        $master = $images[$i]
        $sourceHash = (Get-FileHash -LiteralPath $master.FullName -Algorithm SHA256).Hash
        $image = [System.Drawing.Image]::FromFile($master.FullName)
        try {
            $x = 15 + ($i % $columns) * 250
            $y = 12 + [Math]::Floor($i / $columns) * 420
            $draw.DrawString($master.BaseName, $font, [System.Drawing.Brushes]::Ivory, $x, $y)
            $draw.DrawImage($image, (Fit-Rect $image $x ($y + 38) 220))
            $sizes = @(64, 32, 24)
            $positions = @(0, 92, 154)
            for ($j = 0; $j -lt $sizes.Count; $j++) {
                $size = $sizes[$j]
                $bitmap = [System.Drawing.Bitmap]::new($size, $size)
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.DrawImage($image, (Fit-Rect $image 0 0 $size))
                    $filename = $master.BaseName + '-' + $size + '.png'
                    $destination = Join-Path $exportPath $filename
                    if ([IO.Path]::GetFullPath($destination).Equals($master.FullName, [StringComparison]::OrdinalIgnoreCase)) {
                        throw 'Refusing to overwrite a master.'
                    }
                    $bitmap.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
                    $draw.DrawImageUnscaled($bitmap, $x + $positions[$j], $y + 298 + (64 - $size) / 2)
                    $draw.DrawString(($size.ToString() + ' px'), $smallFont, [System.Drawing.Brushes]::Ivory, $x + $positions[$j], $y + 370)
                    $manifest += [ordered]@{
                        master = $master.Name
                        masterSha256 = $sourceHash
                        export = ('export/' + $filename)
                        width = $size
                        height = $size
                        exportSha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
                    }
                } finally {
                    $graphics.Dispose()
                    $bitmap.Dispose()
                }
            }
        } finally {
            $image.Dispose()
        }
        if ((Get-FileHash -LiteralPath $master.FullName -Algorithm SHA256).Hash -ne $sourceHash) {
            throw ('Master changed during export: ' + $master.Name)
        }
    }
    $draw.DrawString('Offline mechanical export: 64 / 32 / 24 px; source art and colors are unchanged.', $smallFont,
        [System.Drawing.Brushes]::Ivory, 15, $rows * 420 + 5)
    $sheet.Save((Join-Path $outputPath 'contact-sheet.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'export-manifest.json') -Encoding utf8
} finally {
    $draw.Dispose()
    $sheet.Dispose()
    $font.Dispose()
    $smallFont.Dispose()
}
Write-Output ('CONTACT_SHEET_OK: ' + $images.Count + ' unchanged masters; ' + $manifest.Count + ' PNG exports; contact-sheet.png')
