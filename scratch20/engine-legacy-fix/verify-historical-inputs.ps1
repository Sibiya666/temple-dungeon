param([string]$Native='C:/Users/sibiya666/.codex/worktrees/apostate-core/dialoid')
$ErrorActionPreference='Stop'
$bin="$Native/core/Dialoid.Core.Tests/bin/Debug/net8.0"
[AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY',"$bin/")
foreach($file in @('xunit.abstractions.dll','xunit.assert.dll','xunit.core.dll','Dialoid.Core.dll','Dialoid.Core.Tests.dll')){[Reflection.Assembly]::LoadFrom("$bin/$file")|Out-Null}
$data=[Dialoid.Core.Data.DataLoader]::Load("$Native/data").Data
$baselinePath="$Native/core/Dialoid.Core.Tests/Simulation/engine-baseline.json"
$before=(Get-FileHash $baselinePath).Hash
$baseline=Get-Content $baselinePath -Raw|ConvertFrom-Json -AsHashtable
$method=[Dialoid.Core.Tests.Simulation.CombatEngineTests].GetMethod('Fingerprint',[Reflection.BindingFlags]'Static,NonPublic')
$sim=[Dialoid.Core.Simulation.Simulator]::new($data,[Dialoid.Core.Effects.MechanicRegistry]::Default)
$paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$actualKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$checks=0
foreach($row in Get-Content "$Native/core/Dialoid.Core.Tests/Fixtures/legacy_codes.txt"){
 if(!$row){continue};$r=$row.Split("`t"); if(!$paths.Add($r[0])){throw 'Duplicate path'}
 $build=$null;$errorText=$null
 if(![Dialoid.Core.Builds.BuildCodec]::TryImport($r[1],$data,[ref]$build,[ref]$errorText)){throw "$($r[0]): $errorText"}
 if($build.ClassId -notin @('warrior','sorcerer')){throw 'Unexpected class'}
 foreach($dummy in $data.Dummies.Values){foreach($mode in @('average','roll1','roll7')){
  $m=if($mode -eq 'average'){[Dialoid.Core.Simulation.SimMode]::Average}else{[Dialoid.Core.Simulation.SimMode]::Roll}
  $seed=if($mode -eq 'roll7'){7}else{1}
  $config=[Dialoid.Core.Simulation.SimConfig]::new($dummy.Id,$m,$seed,60,0.05,$null,($null -ne $dummy.Attack),180)
  $result=$sim.Run($build,$config)
  $fp=$method.Invoke($null,[object[]]@($result))
  $key="$($r[0])|$($dummy.Id)|$mode";[void]$actualKeys.Add($key)
  if(!$baseline.ContainsKey($key)){throw "Missing baseline $key"}
  foreach($field in $baseline[$key].Keys){if($baseline[$key][$field] -cne $fp[$field]){throw "$key $field differs"};$checks++}
 }}
}
if($paths.Count -ne 32 -or $actualKeys.Count -ne $baseline.Count){throw 'Cohort/run count differs'}
if($before -ne (Get-FileHash $baselinePath).Hash){throw 'Baseline changed'}
[pscustomobject]@{HistoricalPaths=$paths.Count;Runs=$actualKeys.Count;ExactFieldComparisons=$checks;MismatchCount=0;BaselineSha256=$before}|ConvertTo-Json|Set-Content "$PSScriptRoot/historical-verification.json"
