# W29 (v5.1) CI step 2: tests-v4 and tests-v42-filtertut (both .NET 8 console programs), run at the same time.
# Several tests read files through the Cloud Desktop's absolute paths (/home/dev/..., /home/dev/...).
# On Windows such a path means "<current drive>:\local\home\dev\...", so the CI links those folders to the checkout
# (directory junctions; nothing in the tests is changed).
. "$PSScriptRoot/common.ps1"

$dotnet = Get-Dotnet
if ($IsWindows) {
    $drive = [System.IO.Path]::GetPathRoot($Repo).TrimEnd('\')
    $links = @{
        'local\home\dev\tarkov-crosshair-v4'  = (Join-Path $Repo 'tarkov-crosshair-v4');
        'local\home\dev\tarkov-crosshair-dev' = $Dev;
        'local\home\dev\tarkov-shade-module'  = (Join-Path $Repo 'tarkov-shade-module')
    }
    foreach ($k in $links.Keys) {
        $link = Join-Path $drive $k
        if (-not (Test-Path $link)) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $link) | Out-Null
            New-Item -ItemType Junction -Path $link -Target $links[$k] | Out-Null
        }
    }
    $home2 = Join-Path $drive 'home\dev'
    if (-not (Test-Path $home2)) {
        New-Item -ItemType Directory -Force -Path (Join-Path $drive 'home') | Out-Null
        New-Item -ItemType Junction -Path $home2 -Target (Join-Path $drive 'local\home\dev') | Out-Null
    }
}

$t4 = Join-Path $Dev 'tests-v4'
$ft = Join-Path $Dev 'tests-v42-filtertut'
$t4Out = Join-Path $Out 'tests-v4-out'
New-Item -ItemType Directory -Force -Path $t4Out | Out-Null

# both suites at once (separate processes, separate projects)
$jobs = @(
    @{ Name = 'tests-v4'; Args = @('run', '-c', 'Release', '--project', (Join-Path $t4 'Tests.csproj'), '--', $t4Out); Cwd = $t4 },
    @{ Name = 'tests-v42-filtertut'; Args = @('run', '-c', 'Release', '--project', (Join-Path $ft 'FilterTutTests.csproj')); Cwd = $ft }
)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$running = foreach ($j in $jobs) {
    $log = Join-Path $Out ($j.Name + '.log')
    Start-ThreadJob -Name $j.Name -ScriptBlock {
        param($common, $exe, $a, $log, $cwd)
        . $common
        Invoke-Logged $exe $a $log 2400 $cwd
    } -ArgumentList (Join-Path $PSScriptRoot 'common.ps1'), $dotnet, $j.Args, $log, $j.Cwd
}
$codes = @{}
foreach ($r in $running) { $codes[$r.Name] = (Receive-Job -Job $r -Wait -AutoRemoveJob | Select-Object -Last 1) }
$secs = [int]$sw.Elapsed.TotalSeconds

$lines = @()
$allOk = $true
$t = Read-Utf8 (Join-Path $Out 'tests-v4.log')
$m = [regex]::Match($t, 'TOTAL pass=(\d+) fail=(\d+)(?: skip\S*=(\d+))?')
$ok4 = $m.Success -and $m.Groups[2].Value -eq '0' -and $codes['tests-v4'] -eq 0
if ($m.Success) { $lines += "tests-v4: $($m.Value), exit $($codes['tests-v4'])" } else { $lines += "tests-v4: no TOTAL line (build failed?), exit $($codes['tests-v4'])" }
$f = Read-Utf8 (Join-Path $Out 'tests-v42-filtertut.log')
$m2 = [regex]::Match($f, 'filter tutorial tests: (\d+) passed, (\d+) failed')
$okF = $m2.Success -and $m2.Groups[2].Value -eq '0' -and $codes['tests-v42-filtertut'] -eq 0
if ($m2.Success) { $lines += "tests-v42-filtertut: $($m2.Value), exit $($codes['tests-v42-filtertut'])" } else { $lines += "tests-v42-filtertut: no result line, exit $($codes['tests-v42-filtertut'])" }
$allOk = $ok4 -and $okF

# failing test lines (and compiler errors when a suite did not build) for the summary
$fails = @()
foreach ($text in @($t, $f)) {
    $fails += ($text -split "`n" | Where-Object { $_ -match '^FAIL' -or $_ -match ': error [A-Z]+\d+' } | Select-Object -First 40)
}
Write-Utf8 (Join-Path $Out 'tests-fails.txt') ([string]::Join("`n", $fails))
Set-Status 'tests' $allOk (([string]::Join('; ', $lines)) + ", $secs s")
if (-not $allOk) { exit 1 }
