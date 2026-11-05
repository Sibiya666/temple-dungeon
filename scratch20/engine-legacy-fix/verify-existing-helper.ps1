param([string]$Native='C:/Users/sibiya666/.codex/worktrees/apostate-core/dialoid')
$ErrorActionPreference='Stop'
$env:DIALOID_DATA="$Native/data"
$baselinePath="$Native/core/Dialoid.Core.Tests/Simulation/engine-baseline.json"
$baseline=Get-Content $baselinePath -Raw|ConvertFrom-Json -AsHashtable
$legacyPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($key in $baseline.Keys){[void]$legacyPaths.Add($key.Split('|')[0])}
if($legacyPaths.Count -ne 32){throw "Expected32, got $($legacyPaths.Count)"}
$files=@(Get-ChildItem "$Native/data/builds" -Recurse -Filter *.json|Where-Object {(Get-Content $_.FullName -Raw|ConvertFrom-Json).class_id -in @('warrior','sorcerer')}|ForEach-Object {[IO.Path]::GetRelativePath("$Native/data/builds",$_.FullName).Replace([IO.Path]::DirectorySeparatorChar,'/')})
if(@(Compare-Object @($legacyPaths|Sort-Object) @($files|Sort-Object)).Count){throw 'Exact membership differs'}
$bin="$Native/core/Dialoid.Core.Tests/bin/Debug/net8.0"
foreach($file in @('xunit.abstractions.dll','xunit.assert.dll','xunit.core.dll','Dialoid.Core.dll','Dialoid.Core.Tests.dll')){[Reflection.Assembly]::LoadFrom("$bin/$file")|Out-Null}
$type=[Dialoid.Core.Tests.Simulation.CombatEngineTests]
$method=$type.GetMethod('AssertMatchesBaseline',[Reflection.BindingFlags]'Static,NonPublic')
$filter=[Func[string,bool]]{param($name) $legacyPaths.Contains($name)}
$method.Invoke($null,[object[]]@($filter))|Out-Null
[pscustomobject]@{LegacyPaths=$legacyPaths.Count;BaselineRuns=$baseline.Count;FieldsPerRun=@($baseline.Values|ForEach-Object Count|Sort-Object -Unique);Membership='PASS';ExistingBitwiseHelper='PASS';BaselineSha256=(Get-FileHash $baselinePath).Hash}|ConvertTo-Json -Depth 4|Set-Content "$PSScriptRoot/verification.json"
