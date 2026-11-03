#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('generate', 'edit')] [string] $Mode,
    [Parameter(Mandatory)] [string] $PromptFile,
    [string] $Model = '',
    [string] $Image = '',
    [Parameter(Mandatory)] [string] $Out,
    [string] $NegativePromptFile = '',
    [ValidateRange(0, 2147483647)] [int] $Seed = 0,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$stage = 'local validation'
$message = 'Check the supplied files and arguments.'

function Read-Prompt([string] $Path) {
    $Path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    if (-not [IO.File]::Exists($Path)) { throw 'Prompt file does not exist.' }
    $text = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { throw 'Prompt file is empty.' }
    return $text
}

function Get-PngDimensions([byte[]] $Bytes) {
    # Structural validation only; no platform-specific image library is needed.
    [byte[]] $signature = 137,80,78,71,13,10,26,10
    if ($Bytes.Length -lt 57) { throw 'PNG is truncated.' }
    for ($i = 0; $i -lt 8; $i++) {
        if ($Bytes[$i] -ne $signature[$i]) { throw 'Not a PNG image.' }
    }
    $offset = 8L
    $width = 0L; $height = 0L; $hasData = $false; $ended = $false
    while ($offset + 12 -le $Bytes.LongLength) {
        $length = ([long]$Bytes[$offset] -shl 24) -bor ([long]$Bytes[$offset+1] -shl 16) -bor ([long]$Bytes[$offset+2] -shl 8) -bor $Bytes[$offset+3]
        if ($offset + 12 + $length -gt $Bytes.LongLength) { throw 'PNG chunk is truncated.' }
        $kind = [Text.Encoding]::ASCII.GetString($Bytes, [int]$offset + 4, 4)
        if ($offset -eq 8 -and ($kind -ne 'IHDR' -or $length -ne 13)) { throw 'Invalid PNG header.' }
        if ($kind -eq 'IHDR') {
            if ($offset -ne 8) { throw 'Duplicate PNG header.' }
            $width = ([long]$Bytes[16] -shl 24) -bor ([long]$Bytes[17] -shl 16) -bor ([long]$Bytes[18] -shl 8) -bor $Bytes[19]
            $height = ([long]$Bytes[20] -shl 24) -bor ([long]$Bytes[21] -shl 16) -bor ([long]$Bytes[22] -shl 8) -bor $Bytes[23]
            if ($width -le 0 -or $height -le 0) { throw 'Invalid PNG dimensions.' }
        }
        if ($kind -eq 'IDAT' -and $length -gt 0) { $hasData = $true }
        $offset += 12 + $length
        if ($kind -eq 'IEND') {
            if ($length -ne 0 -or $offset -ne $Bytes.LongLength) { throw 'Invalid PNG ending.' }
            $ended = $true; break
        }
    }
    if (-not $ended -or -not $hasData) { throw 'PNG image data or ending is missing.' }
    return @{ width = $width; height = $height }
}

function Write-NewFile([string] $Path, [byte[]] $Bytes) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length) } finally { $stream.Dispose() }
}

try {
    $outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
    if ([IO.Path]::GetExtension($outputPath) -ine '.png') { $message = 'Out must use the .png extension.'; throw $message }
    $outputDirectory = [IO.Path]::GetDirectoryName($outputPath)
    $metadataPath = [IO.Path]::ChangeExtension($outputPath, '.json')
    $rejectedPath = [IO.Path]::Combine($outputDirectory, [IO.Path]::GetFileNameWithoutExtension($outputPath) + '.rejected.png')
    $rejectedMetadataPath = [IO.Path]::ChangeExtension($rejectedPath, '.json')
    foreach ($candidate in @($outputPath, $metadataPath, $rejectedPath, $rejectedMetadataPath)) {
        if (Test-Path -LiteralPath $candidate) { $message = 'Output or sidecar already exists. Choose a new Out filename.'; throw $message }
    }
    if ((Test-Path -LiteralPath $outputDirectory) -and -not [IO.Directory]::Exists($outputDirectory)) {
        $message = 'Output parent is not a directory.'; throw $message
    }
    $message = 'PromptFile must be a nonempty UTF-8 text file.'
    $prompt = Read-Prompt $PromptFile
    $negative = ''
    if ($NegativePromptFile) { $negative = Read-Prompt $NegativePromptFile }
    if ([string]::IsNullOrWhiteSpace($Model)) { $Model = if ($Mode -eq 'generate') { 'seedream-v5-pro' } else { 'seedream-v5-pro-edit' } }
    $seedSent = $Mode -eq 'generate' -and $Seed -gt 0
    $reference = $null
    if ($Mode -eq 'generate') {
        if ($Image) { $message = 'Image is only supported in edit mode.'; throw $message }
        $body = [ordered]@{
            model = $Model; prompt = $prompt; width = 1024; height = 1024
            format = 'png'; hide_watermark = $true; safe_mode = $true; return_binary = $false
        }
        if ($negative) { $body.negative_prompt = $negative }
        if ($seedSent) { $body.seed = $Seed }
        $endpoint = 'https://api.venice.ai/api/v1/image/generate'
    } else {
        if ($NegativePromptFile -or $PSBoundParameters.ContainsKey('Seed')) {
            $message = 'Seed and NegativePromptFile are only supported in generate mode.'; throw $message
        }
        $message = 'Edit mode requires an existing structurally valid PNG Image.'
        if (-not $Image) { throw $message }
        $imagePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Image)
        if (-not [IO.File]::Exists($imagePath)) { throw $message }
        $imageBytes = [IO.File]::ReadAllBytes($imagePath)
        $null = Get-PngDimensions $imageBytes
        $relative = [IO.Path]::GetRelativePath($outputDirectory, $imagePath)
        $reference = if ($relative -eq '..' -or $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -or [IO.Path]::IsPathRooted($relative)) {
            [IO.Path]::GetFileName($imagePath)
        } else { $relative.Replace('\', '/') }
        $body = [ordered]@{ model = $Model; prompt = $prompt; image = [Convert]::ToBase64String($imageBytes); aspect_ratio = '1:1' }
        $endpoint = 'https://api.venice.ai/api/v1/image/edit'
    }
    $json = $body | ConvertTo-Json -Depth 5 -Compress
    if ($DryRun) {
        $summary = [ordered]@{ dryRun = $true; mode = $Mode; model = $Model; promptCharacters = $prompt.Length; seedSent = $seedSent }
        if ($Mode -eq 'generate') { $summary.width = 1024; $summary.height = 1024 }
        else { $summary.aspectRatio = '1:1'; $summary.referenceBytes = $imageBytes.Length }
        $summary | ConvertTo-Json -Compress
        exit 0
    }

    $stage = 'credentials'; $message = 'Set your own VENICE_API_KEY in this process environment; no credential files are read.'
    $key = [Environment]::GetEnvironmentVariable('VENICE_API_KEY', 'Process')
    if ([string]::IsNullOrWhiteSpace($key)) { throw $message }
    $stage = 'request'; $message = 'Request failed. No retry was attempted; check account usage before manually retrying.'
    $headers = @{ Authorization = 'Bearer ' + $key }
    $response = Invoke-WebRequest -Uri $endpoint -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($json)) -MaximumRetryCount 0 -TimeoutSec 180 -SkipHttpErrorCheck
    if ([int]$response.StatusCode -lt 200 -or [int]$response.StatusCode -ge 300) {
        $message = 'HTTP status ' + [int]$response.StatusCode + '. No retry was attempted; review account usage before retrying.'
        throw $message
    }
    $stage = 'response validation'; $message = 'Unexpected response type or invalid PNG/JSON. No retry was attempted.'
    $contentType = [string]::Join(',', @($response.Headers['Content-Type']))
    if ($Mode -eq 'generate') {
        if ($contentType -notmatch '(?i)application/json') { throw $message }
        $data = $response.Content | ConvertFrom-Json
        if (-not $data.images -or $data.images.Count -lt 1) { throw $message }
        $png = [Convert]::FromBase64String([string]$data.images[0])
    } else {
        if ($contentType -notmatch '(?i)image/png') { throw $message }
        $png = if ($response.Content -is [byte[]]) { $response.Content } else { $response.RawContentStream.ToArray() }
    }
    $dimensions = Get-PngDimensions $png
    if ($dimensions.width -ne $dimensions.height -or ($Mode -eq 'generate' -and $dimensions.width -ne 1024)) { throw $message }
    $blurred = 'unknown'
    $blurHeader = ([string]::Join(',', @($response.Headers['x-venice-is-blurred']))).Trim()
    if ($blurHeader -match '(?i)^true$') { $blurred = 'true' }
    elseif ($blurHeader -match '(?i)^false$') { $blurred = 'false' }
    $status = if ($blurred -eq 'true') { 'rejected_blurred' } else { 'unreviewed' }
    $metadata = [ordered]@{
        service = 'Venice API'; mode = $Mode; model = $Model; prompt = $prompt; negativePrompt = $negative
        reference = $reference; dateUtc = [DateTimeOffset]::UtcNow.ToString('O')
        seedSent = $seedSent; seed = $(if ($seedSent) { $Seed } else { $null })
        width = $dimensions.width; height = $dimensions.height; blurred = $blurred; status = $status
        rightsStatus = 'not_verified'
    }
    $stage = 'saving output'; $message = 'Could not save new output/metadata. Inspect local files before another request; no retry was attempted.'
    $null = [IO.Directory]::CreateDirectory($outputDirectory)
    if ($blurred -eq 'true') { $outputPath = $rejectedPath; $metadataPath = $rejectedMetadataPath }
    Write-NewFile $outputPath $png
    Write-NewFile $metadataPath ([Text.Encoding]::UTF8.GetBytes(($metadata | ConvertTo-Json -Depth 5) + "`n"))
    if ($blurred -eq 'true') { $stage = 'content review'; $message = 'blurred=true: rejected files saved with .rejected names; this is not an accepted asset.'; throw $message }
    Write-Output ('SAVED ' + [IO.Path]::GetFileName($outputPath) + ' status=unreviewed blurred=' + $blurred)
} catch {
    # Deliberately do not emit server bodies, headers, keys or the original exception.
    Write-Error -Message ('Venice tool stopped during ' + $stage + '. ' + $message) -ErrorAction Continue
    exit 1
}
