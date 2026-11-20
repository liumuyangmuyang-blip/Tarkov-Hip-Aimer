# W29 (v5.1) CI step 4: the layout / text-fit probe (ci/probe/UiCProbe.cs, built by build.ps1 as RenderDriver.exe) on real
# Windows at every screen size x Windows display scale the runner can do. For each combination DpiTool.exe switches the
# resolution and the display scale, then each probe mode runs in a NEW process (so it starts at that DPI):
#   settings   every settings page, basic / advanced, 4 window sizes: cells, overlaps, text fits its box, fonts fixed
#   dialogs    every message dialog text, name dialogs        tutorial / filtertut   every tutorial step
#   windows    every other window (v5.1 wizard, 必读, 常见问题 ...)     timing   settings window build / show / page open ms
# Screenshots go to ci-out/shots, results to ci-out/probe. A combination Windows does not allow (e.g. 1280x720 at 150 %:
# Windows offers at most 100 % there) or a size the runner's display adapter does not offer is listed as NOT RUN, never
# counted as passed. The step fails if any probe check fails, a run crashes, or a requested scale never ran at all.
#   pwsh probe.ps1 [-Sizes 1280x720,1920x1080,...] [-Scales 100,150,200] [-Modes settings,dialogs,...]
#   pwsh probe.ps1 -AggregateOnly -ResultDir <dir>    re-read existing res-*.txt files (local check)
param(
    [string]$Sizes = '1280x720,1920x1080,2160x1440,2560x1440',
    [string]$Scales = '100,150,200',
    [string]$Modes = 'timing,timingjit,settings,dialogs,tutorial,filtertut,windows',
    [int]$ModeTimeoutSec = 1200,
    # screenshots: 'pages' = settings pages + other windows (default, keeps the artifact small), 'all' = also every
    # tutorial step and dialog, 'none'
    [ValidateSet('pages', 'all', 'none')][string]$Shots = 'pages',
    [switch]$AggregateOnly,
    [string]$ResultDir = ''
)
. "$PSScriptRoot/common.ps1"

$probeOut = Join-Path $Out 'probe'
if ($ResultDir) { $probeOut = $ResultDir }
# (PowerShell names ignore case: $shots would be the validated -Shots parameter and crash with MetadataError)
$shotDir = Join-Path $Out 'shots'
New-Item -ItemType Directory -Force -Path $probeOut, $shotDir | Out-Null
$notRun = New-Object System.Collections.Generic.List[string]
$scaleList = @($Scales -split ',' | ForEach-Object { [int]$_.Trim() })

if (-not $AggregateOnly) {
    if (-not $IsWindows) { Set-Status 'probe' $false 'the layout probe needs Windows'; exit 1 }
    $probeDir = Join-Path $Work 'probe'
    $probeExe = Join-Path $probeDir 'RenderDriver.exe'
    $dpiTool = Join-Path $Work 'tools/DpiTool.exe'
    if (-not (Test-Path $probeExe) -or -not (Test-Path $dpiTool)) { Set-Status 'probe' $false 'probe or DpiTool missing (build step failed)'; exit 1 }
    # system-DPI-aware programs started after a scale change get the new DPI (the Settings app's "fix blurry apps" switch)
    New-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name 'EnablePerProcessSystemDPI' -Value 1 -PropertyType DWord -Force | Out-Null
    $modesTxt = @(& $dpiTool modes)
    $offered = @($modesTxt | Where-Object { $_ -like 'mode *' } | ForEach-Object { $_.Substring(5).Trim() })
    $startRes = (@($modesTxt | Where-Object { $_ -like 'current *' }) -join '') -replace 'current ', ''
    Write-Utf8 (Join-Path $Out 'display-modes.txt') ([string]::Join("`n", $modesTxt))
    Write-Host "display adapter offers: $($offered -join ', ') (now $startRes)"
    $env:UIC_SHOTS_ALL = '1'
    $env:UIC_WINMETRICS = '0'
    $env:UIC_DIALOGS = Join-Path $probeDir 'dialogs.tsv'
    try {
        foreach ($size in @($Sizes -split ',' | ForEach-Object { $_.Trim() })) {
            if ($offered -notcontains $size) { $notRun.Add("$size (all scales): the runner's display adapter does not offer this resolution"); continue }
            $wh = $size -split 'x'
            $o = & $dpiTool res $wh[0] $wh[1]
            if ($LASTEXITCODE -ne 0) { $notRun.Add("$size (all scales): Windows refused the resolution ($o)"); continue }
            Start-Sleep -Seconds 2
            foreach ($s in $scaleList) {
                $o = @(& $dpiTool set $s)
                if ($LASTEXITCODE -ne 0) { $g = @(& $dpiTool get); $notRun.Add("$size@$s%: $($o -join ' ') [$($g -join ' ')]"); continue }
                Start-Sleep -Seconds 3
                $env:UIC_EXPECT_DPI = [string][int][math]::Round(96 * $s / 100)
                foreach ($mode in @($Modes -split ',' | ForEach-Object { $_.Trim() })) {
                    $res = Join-Path $probeOut "res-$size-$s-$mode.txt"
                    $log = Join-Path $probeOut "log-$size-$s-$mode.txt"
                    Write-Host "probe $size@$s% $mode"
                    $pa = @($res, $mode)
                    if ($Shots -eq 'all' -or ($Shots -eq 'pages' -and ($mode -eq 'settings' -or $mode -eq 'windows'))) { $pa += $shotDir }
                    $rc = Invoke-Logged $probeExe $pa $log $ModeTimeoutSec $probeDir
                    if (-not (Test-Path $res) -or (Get-Item $res).Length -eq 0) {
                        Write-Utf8 $res "RESULT $size@$s% mode=$mode NO-RESULT (exit $rc, see log-$size-$s-$mode.txt)`n"
                    }
                }
            }
        }
    }
    finally {
        & $dpiTool set 100 | Out-Null
        if ($startRes -match '^\d+x\d+$') { $wh = $startRes -split 'x'; & $dpiTool res $wh[0] $wh[1] | Out-Null }
    }
}

# ---------------------------------------------------------------- aggregate
$rows = New-Object System.Collections.Generic.List[object]
$failLines = New-Object System.Collections.Generic.List[string]
$timeLines = New-Object System.Collections.Generic.List[string]
foreach ($f in @(Get-ChildItem -Path $probeOut -Filter 'res-*.txt' -File | Sort-Object Name)) {
    foreach ($line in ((Read-Utf8 $f.FullName) -split "`n")) {
        $line = $line.TrimEnd("`r")
        if ($line -match '^RESULT (\S+) mode=(\S+) pass=(\d+) fail=(\d+)') {
            $rows.Add([pscustomobject]@{ Tag = $Matches[1]; Mode = $Matches[2]; Pass = [int]$Matches[3]; Fail = [int]$Matches[4]; Note = '' })
        }
        elseif ($line -match '^RESULT (\S+) mode=(\S+) (NO-RESULT.*)$') {
            $rows.Add([pscustomobject]@{ Tag = $Matches[1]; Mode = $Matches[2]; Pass = 0; Fail = 1; Note = $Matches[3] })
        }
        elseif ($line.StartsWith('FAIL ')) { $failLines.Add(($f.BaseName -replace '^res-', '') + ' | ' + $line) }
        elseif ($line.StartsWith('TIME ')) { $timeLines.Add($line.Substring(5)) }
    }
}
Write-Utf8 (Join-Path $Out 'probe-fails.txt') ([string]::Join("`n", $failLines))
Write-Utf8 (Join-Path $Out 'probe-times.txt') ([string]::Join("`n", $timeLines))

$ran = @{}
foreach ($r in $rows) { if ($r.Tag -match '@(\d+)%') { $ran[[int]$Matches[1]] = $true } }
$missing = @($scaleList | Where-Object { -not $ran.ContainsKey($_) })
$failed = @($rows | Where-Object { $_.Fail -gt 0 })
$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine('### Layout / text-fit probe (real Windows)')
[void]$md.AppendLine('')
[void]$md.AppendLine('| screen @ scale | mode | checks passed | failed |')
[void]$md.AppendLine('|---|---|---:|---:|')
foreach ($r in $rows) { [void]$md.AppendLine("| $($r.Tag) | $($r.Mode) | $($r.Pass) | $($r.Fail) $($r.Note) |") }
if ($notRun.Count -gt 0) {
    [void]$md.AppendLine('')
    [void]$md.AppendLine('**Not run** (not counted as passed):')
    foreach ($n in $notRun) { [void]$md.AppendLine("- $n") }
}
if ($missing.Count -gt 0) { [void]$md.AppendLine(''); [void]$md.AppendLine("**No run at all at: $(($missing | ForEach-Object { "$_%" }) -join ', ')** (the runner could not switch to it)") }
if ($failLines.Count -gt 0) {
    [void]$md.AppendLine('')
    [void]$md.AppendLine('First failures (all of them: probe-fails.txt in the artifact):')
    [void]$md.AppendLine('```')
    foreach ($l in ($failLines | Select-Object -First 25)) { [void]$md.AppendLine($l.Substring(0, [Math]::Min(400, $l.Length))) }
    [void]$md.AppendLine('```')
}
Write-Utf8 (Join-Path $Out 'probe-summary.md') $md.ToString()
Write-Utf8 (Join-Path $Out 'probe-notrun.txt') ([string]::Join("`n", $notRun))
$ok = ($rows.Count -gt 0) -and ($failed.Count -eq 0) -and ($missing.Count -eq 0)
$sum = 0; foreach ($r in $rows) { $sum += $r.Pass }
Set-Status 'probe' $ok ("$($rows.Count) runs, $sum checks passed, $($failed.Count) runs with failures, $($notRun.Count) not run" + $(if ($missing.Count) { ", no run at " + (($missing | ForEach-Object { "$_%" }) -join '/') } else { '' }))
if (-not $ok) { exit 1 }
