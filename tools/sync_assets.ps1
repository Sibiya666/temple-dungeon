# Copies the room assets listed in game/data/assets.json into game/vendor/ (not in git) so that Godot can import
# them inside res:// (spec §9). Only the files from the table are copied, not the whole packs.
#
# Where the assets are: $env:DIALOID_ASSETS, else assets/ in the repository root.
# File names are normalized: lower case, spaces and brackets -> "_", repeated "_" collapsed, no "_" before the
# extension ("sword and shield slash (2).fbx" -> "sword_and_shield_slash_2.fbx"); game/Room/AssetLibrary.cs uses
# the same rule. A file is copied when it is missing in vendor/ or the source is newer or of another size.
# vendor/ mirrors the table: a vendor file whose source is gone is removed (the room then draws a stub).
# Textures get a .import file with VRAM compression and mipmaps (spec §4: 2K textures, VRAM compression).
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/sync_assets.ps1
# Exit code: 0 always, unless the table itself is missing or broken (1). Without assets: 0 and a warning line.

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$Game = Join-Path $Root 'game'
$Table = Join-Path $Game 'data\assets.json'
$Vendor = Join-Path $Game 'vendor'

function Say([string]$Text) { [Console]::Out.WriteLine($Text) }

function Normalize-Name([string]$Name) {
    $n = $Name.ToLowerInvariant() -replace '[\s\(\)\[\]]', '_'
    $n = $n -replace '_+', '_'
    $n = $n -replace '_+(\.[^.]+)$', '$1'
    return $n.Trim('_')
}

function Normalize-Path([string]$Relative) {
    return (($Relative -split '[\\/]') | Where-Object { $_ } | ForEach-Object { Normalize-Name $_ }) -join '\'
}

if (-not (Test-Path -LiteralPath $Table -PathType Leaf)) {
    [Console]::Error.WriteLine("Нет таблицы ассетов $Table")
    exit 1
}
try {
    $json = Get-Content -LiteralPath $Table -Encoding UTF8 -Raw | ConvertFrom-Json
} catch {
    [Console]::Error.WriteLine("Таблица ассетов $Table не читается: $($_.Exception.Message)")
    exit 1
}

$AssetsRoot = if ($env:DIALOID_ASSETS) { $env:DIALOID_ASSETS } else { Join-Path $Root 'assets' }

# Every file named by the table (relative to the assets folder), with its kind.
$files = [ordered]@{}
foreach ($p in $json.characters.PSObject.Properties) { $files[[string]$p.Value.file] = 'model' }
foreach ($p in $json.clips.PSObject.Properties) { $files[[string]$p.Value.file] = 'model' }
foreach ($p in $json.surfaces.PSObject.Properties) {
    $files[[string]$p.Value.albedo] = 'albedo'
    $files[[string]$p.Value.normal] = 'normal'
    $files[[string]$p.Value.roughness] = 'roughness'
}

function Texture-Import([string]$Kind) {
    $normal = if ($Kind -eq 'normal') { 1 } else { 2 }
    return @"
[remap]

importer="texture"
type="CompressedTexture2D"

[params]

compress/mode=2
compress/high_quality=false
compress/normal_map=$normal
compress/channel_pack=0
mipmaps/generate=true
detect_3d/compress_to=0
"@
}

New-Item -ItemType Directory -Force -Path $Vendor | Out-Null

$found = @()
$missing = @()
$copied = 0
$expected = @{}
foreach ($rel in $files.Keys) {
    $src = Join-Path $AssetsRoot $rel
    $dstRel = Normalize-Path $rel
    $dst = Join-Path $Vendor $dstRel
    if (-not (Test-Path -LiteralPath $src -PathType Leaf)) {
        $missing += $rel
        continue
    }
    $found += $rel
    $expected[$dst.ToLowerInvariant()] = $true
    $srcInfo = Get-Item -LiteralPath $src
    $dstInfo = Get-Item -LiteralPath $dst -ErrorAction SilentlyContinue
    if (-not $dstInfo -or $srcInfo.LastWriteTimeUtc -gt $dstInfo.LastWriteTimeUtc -or $srcInfo.Length -ne $dstInfo.Length) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $copied++
    }
    $kind = $files[$rel]
    if ($kind -ne 'model') {
        $import = "$dst.import"
        $text = if (Test-Path -LiteralPath $import) { Get-Content -LiteralPath $import -Raw } else { '' }
        if ($text -notmatch 'compress/mode=2') {
            [IO.File]::WriteAllText($import, (Texture-Import $kind), (New-Object Text.UTF8Encoding $false))
        }
    }
}

# vendor/ mirrors the table: drop files whose source is gone. Kept next to a found file: its .import and the
# textures Godot extracts from a found FBX (<name>_0.png and their .import).
$keepPrefixes = @($expected.Keys | Where-Object { $_.EndsWith('.fbx') } | ForEach-Object { $_.Substring(0, $_.Length - 4) + '_' })
$removed = 0
foreach ($f in Get-ChildItem -LiteralPath $Vendor -Recurse -File) {
    $full = $f.FullName.ToLowerInvariant()
    $base = if ($full.EndsWith('.import')) { $full.Substring(0, $full.Length - 7) } else { $full }
    if ($expected.ContainsKey($base)) { continue }
    if ($keepPrefixes | Where-Object { $base.StartsWith($_) } | Select-Object -First 1) { continue }
    Remove-Item -LiteralPath $f.FullName -Force
    $removed++
}

if ($found.Count -eq 0) {
    Say "sync_assets: ассеты не найдены, будут заглушки (папка: $AssetsRoot)"
} else {
    Say "sync_assets: найдено $($found.Count) из $($files.Count) файлов в $AssetsRoot, скопировано $copied, удалено из vendor $removed"
    foreach ($m in $missing) { Say "sync_assets: нет файла $m" }
}
if (-not (Test-Path -LiteralPath (Join-Path $AssetsRoot 'LICENSES.md') -PathType Leaf)) {
    Say "sync_assets: ВНИМАНИЕ: нет учета лицензий LICENSES.md в $AssetsRoot — сборки с ассетами не публикуются"
}
exit 0
