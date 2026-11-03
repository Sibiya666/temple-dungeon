#requires -Version 7.0
[CmdletBinding()]
param([string]$Out = '')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)
if(!$Out){$Out=Join-Path (Split-Path -Parent $root) 'Dialoid-Art-Pipeline-for-Claude-2026-11-03.zip'}
$archivePath=[IO.Path]::GetFullPath($Out)
if(Test-Path -LiteralPath $archivePath){throw 'Archive already exists. Choose another -Out; existing files are not overwritten.'}
$allowedRoots=@('examples/','scripts/','claude-skill/')
$allowedTop=@('START_HERE.md','PROCESS.md','PROMPTS.md','CHECKLIST.md','CLAUDE.md','BUILD_PACKAGE.ps1','PACKAGE_CHECKS.md')
foreach($required in $allowedTop){if(!(Test-Path -LiteralPath (Join-Path $root $required))){throw ('Missing package document: '+$required)}}
$all=Get-ChildItem -LiteralPath $root -Recurse -File
$selected=@($all | Where-Object {
 $rel=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')
 ($rel -in $allowedTop) -or ($allowedRoots | Where-Object {$rel.StartsWith($_,[StringComparison]::Ordinal)})
} | Sort-Object FullName)
foreach($file in $selected){
 if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Symlink/reparse files are not packaged.'}
 if($file.Name -eq '.env' -or $file.Extension -in @('.log','.zip','.pfx','.pem','.key')){throw ('Unexpected private/artifact file: '+$file.Name)}
}
$manifest=Join-Path $root 'MANIFEST.sha256'
$manifestLines=@($selected | ForEach-Object {
 $rel=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')
 (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$rel
})
[IO.File]::WriteAllText($manifest,($manifestLines -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
$selected+=Get-Item -LiteralPath $manifest
$prefix='Dialoid-Art-Pipeline-for-Claude/'
$archive=[IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($file in $selected){
  $rel=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$prefix+$rel,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
 }
}finally{$archive.Dispose()}
$verify=[IO.Compression.ZipFile]::OpenRead($archivePath)
try{
 if($verify.Entries.Count -ne $selected.Count){throw 'Archive entry count mismatch.'}
 foreach($entry in $verify.Entries){
  $rel=$entry.FullName.Substring($prefix.Length)
  if(!$entry.FullName.StartsWith($prefix) -or $rel.Contains('..') -or [IO.Path]::IsPathRooted($rel)){throw 'Unsafe archive path.'}
  $stream=$entry.Open()
  try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
  $expected=(Get-FileHash -LiteralPath (Join-Path $root $rel) -Algorithm SHA256).Hash
  if($actual -ne $expected){throw ('Archive content mismatch: '+$rel)}
 }
}finally{$verify.Dispose()}
$zip=Get-Item -LiteralPath $archivePath
Write-Output ('PACKAGE_OK: '+$selected.Count+' files; '+$zip.Length+' bytes; '+$archivePath)
