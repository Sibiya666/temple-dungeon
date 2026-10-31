# Build and headless check of the Godot debug scene (spec §3.2), Windows PowerShell 5.1 version
# of tools/godot_check.sh (same steps, limits and exit codes):
#   dotnet build -> tools/sync_assets.ps1 (room assets into game/vendor/) -> godot --headless --import
#   -> godot --headless --quit (or -- --selftest),
# then the log is checked. Every step runs under a time limit.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot_check.ps1
#          [-SelfTest] [-ExpectLoadFailure] [-Screenshots <dir>] [-Perf] [-Quality low|high] [-ImportTimeoutSec <n>]
#   -SelfTest             run the scene's self-test instead of a plain start
#   -ExpectLoadFailure    the scene must report a data load failure (marker DIALOID_LOAD_FAILED:)
#                         and the log must still have no ERROR lines; used for the missing-data check
#   -Screenshots <dir>    run the self-test in a real window (not headless) and save screenshots to <dir>
#   -Perf                 the load run (spec §10.8): in a real window, only the measurement; prints «SELFTEST PERF …»
#   -Quality low|high     graphics quality of the self-test, screenshot or load run (passed as «-- --quality …»)
#   -ImportTimeoutSec <n> time limit of the import step, seconds (default 600: the first import
#                         of dozens of FBX files is long)
#
# Exit codes:
#   0        success
#   1        build or import failed, ERROR / SCRIPT ERROR line in the Godot log,
#            or -ExpectLoadFailure given but the scene loaded its data
#   2        Godot not found or bad arguments
#   3        the scene could not load the game data (marker DIALOID_LOAD_FAILED: in the log)
#   other    the self-test's own exit code (checked before the log)
#   124      a step hit its time limit (the process and its children are killed)
#
# Arguments are parsed by hand (no param block) so that any bad argument gives exit code 2.

$ErrorActionPreference = 'Stop'

# The self-test's developer filter (DIALOID_SELFTEST_FILTER) never applies to a check: every step runs.
Remove-Item Env:DIALOID_SELFTEST_FILTER -ErrorAction SilentlyContinue

$Root = Split-Path -Parent $PSScriptRoot
$Game = Join-Path $Root 'game'
$Marker = 'DIALOID_LOAD_FAILED:'
$DefaultGodot = 'C:\Users\sibiya666\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'

function Write-Err([string]$Message) {
    [Console]::Error.WriteLine($Message)
}

function Fail-Args([string]$Message) {
    Write-Err "$Message (допустимы -SelfTest, -ExpectLoadFailure, -Screenshots <папка>, -Perf, -Quality low|high, -ImportTimeoutSec <секунды>)"
    exit 2
}

$SelfTest = $false
$ExpectLoadFailure = $false
$Screenshots = $null
$Perf = $false
$Quality = $null
$ImportTimeoutSec = 600

$i = 0
while ($i -lt $args.Count) {
    $arg = [string]$args[$i]
    switch ($arg) {
        '-SelfTest' { $SelfTest = $true }
        '-ExpectLoadFailure' { $ExpectLoadFailure = $true }
        '-Perf' { $Perf = $true }
        '-Quality' {
            $i++
            if ($i -ge $args.Count -or @('low', 'high') -notcontains ([string]$args[$i])) { Fail-Args '-Quality требует low или high' }
            $Quality = [string]$args[$i]
        }
        '-Screenshots' {
            $i++
            if ($i -ge $args.Count -or ([string]$args[$i]).StartsWith('-')) { Fail-Args 'Не указана папка для -Screenshots' }
            $Screenshots = [string]$args[$i]
        }
        '-ImportTimeoutSec' {
            $i++
            $n = 0
            if ($i -ge $args.Count -or -not [int]::TryParse([string]$args[$i], [ref]$n) -or $n -le 0) {
                Fail-Args '-ImportTimeoutSec требует целое число секунд больше нуля'
            }
            $ImportTimeoutSec = $n
        }
        default { Fail-Args "Неизвестный аргумент: $arg" }
    }
    $i++
}

# Godot binary: $env:GODOT, else the default Windows path, else godot in PATH.
$Godot = $null
if ($env:GODOT) {
    if (Test-Path -LiteralPath $env:GODOT -PathType Leaf) {
        $Godot = $env:GODOT
    } else {
        $cmd = Get-Command $env:GODOT -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cmd) { $Godot = $cmd.Source }
    }
} elseif (Test-Path -LiteralPath $DefaultGodot -PathType Leaf) {
    $Godot = $DefaultGodot
} else {
    $cmd = Get-Command godot -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { $Godot = $cmd.Source }
}
if (-not $Godot) {
    Write-Err "Godot 4 .NET не найден. Укажите путь к консольному exe в переменной GODOT (сейчас: '$env:GODOT'),"
    Write-Err "или установите его в $DefaultGodot, или добавьте godot в PATH."
    exit 2
}

$Dotnet = 'dotnet'
if (-not (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue)) {
    $Dotnet = 'C:\Program Files\dotnet\dotnet.exe'
}

if ($Screenshots) {
    $Screenshots = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Screenshots)
}

function Quote([string]$s) {
    if ($s -match '[\s"]') { return '"' + ($s -replace '"', '\"') + '"' }
    return $s
}

# Run-Limited: runs a process under a time limit, prints nothing, returns @{ Code; Output }.
# Code 124 = time limit hit (the whole process tree is killed with taskkill /T /F).
function Run-Limited([int]$Seconds, [string]$File, [string[]]$Arguments) {
    $out = [IO.Path]::GetTempFileName()
    $err = [IO.Path]::GetTempFileName()
    try {
        $argLine = ($Arguments | ForEach-Object { Quote $_ }) -join ' '
        $p = Start-Process -FilePath $File -ArgumentList $argLine -NoNewWindow -PassThru `
            -RedirectStandardOutput $out -RedirectStandardError $err
        # PowerShell 5.1: touch the handle right away, otherwise ExitCode may be $null after WaitForExit.
        $null = $p.Handle
        if ($p.WaitForExit($Seconds * 1000)) {
            $p.WaitForExit()
            $code = $p.ExitCode
            if ($null -eq $code) { $code = 1 }
        } else {
            & taskkill.exe /T /F /PID $p.Id 2>&1 | Out-Null
            try { $p.WaitForExit(10000) | Out-Null } catch { }
            $code = 124
        }
        $text = ''
        foreach ($f in @($out, $err)) {
            $c = Get-Content -LiteralPath $f -Encoding UTF8 -Raw -ErrorAction SilentlyContinue
            if ($c) {
                if ($text -and -not $text.EndsWith("`n")) { $text += "`n" }
                $text += $c
            }
        }
        return @{ Code = [int]$code; Output = $text }
    } finally {
        Remove-Item -LiteralPath $out, $err -Force -ErrorAction SilentlyContinue
    }
}

function Print([string]$Text) {
    if ($Text) { [Console]::Out.Write($Text); if (-not $Text.EndsWith("`n")) { [Console]::Out.WriteLine() } }
}

# Fail-Step: a timeout keeps its code, any other failure is 1.
function Fail-Step([int]$Code, [string]$Message) {
    Write-Err "$Message (код $Code)"
    if ($Code -eq 124) { exit 124 }
    exit 1
}

function Step([string]$Name) { [Console]::Out.WriteLine("== $Name") }

Step 'dotnet build'
$r = Run-Limited 600 $Dotnet @('build', (Join-Path $Game 'Dialoid.Game.csproj'), '-nologo', '-v', 'quiet')
Print $r.Output
if ($r.Code -ne 0) { Fail-Step $r.Code 'Сборка не прошла' }

$Log = ''

# Room assets (not in git) are copied into game/vendor/ before the import; without them the room draws stubs.
Step 'sync assets'
& (Join-Path $PSScriptRoot 'sync_assets.ps1')
if ($LASTEXITCODE -ne 0) { Fail-Step $LASTEXITCODE 'Синхронизация ассетов не прошла' }

Step 'godot --import'
$r = Run-Limited $ImportTimeoutSec $Godot @('--headless', '--path', $Game, '--import')
$Log += $r.Output
if ($r.Code -ne 0) {
    Print $Log
    Fail-Step $r.Code 'godot --import завершился с ошибкой'
}

$QualityArgs = @()
if ($Quality) { $QualityArgs = @('--quality', $Quality) }
if ($Screenshots) {
    Step 'godot -- --selftest --screenshots (в окне)'
    $r = Run-Limited 600 $Godot (@('--path', $Game, '--', '--selftest', '--screenshots', $Screenshots) + $QualityArgs)
} elseif ($Perf) {
    Step 'godot -- --selftest --perf (в окне)'
    $r = Run-Limited 300 $Godot (@('--path', $Game, '--', '--selftest', '--perf') + $QualityArgs)
} elseif ($SelfTest) {
    Step 'godot -- --selftest'
    $r = Run-Limited 300 $Godot (@('--headless', '--path', $Game, '--', '--selftest') + $QualityArgs)
} else {
    Step 'godot --quit'
    $r = Run-Limited 60 $Godot @('--headless', '--path', $Game, '--quit')
}
if ($Log -and -not $Log.EndsWith("`n")) { $Log += "`n" }
$Log += $r.Output
$code = $r.Code

Print $Log

# The Godot run's own code (self-test result or timeout) wins over the log checks.
if ($code -ne 0) {
    Write-Err "Godot завершился с кодом $code"
    exit $code
}

# Godot colors its output: strip ANSI codes, then match only Godot's own error lines.
$esc = [string][char]27
$lines = ($Log -replace ($esc + '\[[0-9;]*m'), '') -split "`r?`n"
if ($lines | Where-Object { $_ -cmatch '^(SCRIPT )?ERROR:' } | Select-Object -First 1) {
    Write-Err 'В логе Godot есть ERROR'
    exit 1
}

if ($lines | Where-Object { $_.StartsWith($Marker, [StringComparison]::Ordinal) } | Select-Object -First 1) {
    if ($ExpectLoadFailure) {
        [Console]::Out.WriteLine('OK (ожидаемая ошибка загрузки данных)')
        exit 0
    }
    Write-Err 'Сцена не загрузила данные игры'
    exit 3
}
if ($ExpectLoadFailure) {
    Write-Err 'Ожидалась ошибка загрузки данных, но сцена загрузила их'
    exit 1
}
[Console]::Out.WriteLine('OK')
exit 0
