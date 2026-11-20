# W29 (v5.1) CI last step: one page with every result. Written to ci-out/SUMMARY.md (in the artifact) and to the run's
# Summary tab on GitHub. Exit 1 (red run) when any step failed or did not run.
. "$PSScriptRoot/common.ps1"

$steps = @(
    @{ Id = 'build'; Name = 'Build 准星 TarkovHipAimer.exe (0 errors, 0 warnings)' },
    @{ Id = 'tests'; Name = 'Tests (tests-v4, tests-v42-filtertut)' },
    @{ Id = 'timing'; Name = 'Startup and settings window timing' },
    @{ Id = 'probe'; Name = 'Layout / text-fit probe at 100 / 150 / 200 %' }
)
$md = New-Object System.Text.StringBuilder
$sha = $env:GITHUB_SHA
if ($sha) { $sha = $sha.Substring(0, [Math]::Min(8, $sha.Length)) } else { $sha = 'local' }
[void]$md.AppendLine("## Tarkov Hip Aimer Windows CI ($sha)")
[void]$md.AppendLine('')
[void]$md.AppendLine('| step | result | detail |')
[void]$md.AppendLine('|---|---|---|')
$allOk = $true
foreach ($s in $steps) {
    $line = Read-Utf8 (Join-Path $Out "status-$($s.Id).txt")
    $word = 'NOT RUN'; $detail = 'the step did not finish (see its log on the run page)'
    if ($line -match '^(PASS|FAIL) (.*)$') { $word = $Matches[1]; $detail = $Matches[2] }
    if ($word -ne 'PASS') { $allOk = $false }
    $mark = '❌'
    if ($word -eq 'PASS') { $mark = '✅' }
    [void]$md.AppendLine("| $($s.Name) | $mark $word | $($detail -replace '\|', '/') |")
}
$tf = Read-Utf8 (Join-Path $Out 'tests-fails.txt')
if ($tf.Trim()) {
    [void]$md.AppendLine('')
    [void]$md.AppendLine('### Failing tests (first 40)')
    [void]$md.AppendLine('```')
    [void]$md.AppendLine($tf.Trim())
    [void]$md.AppendLine('```')
}
foreach ($part in 'timing-summary.md', 'probe-summary.md') {
    $t = Read-Utf8 (Join-Path $Out $part)
    if ($t.Trim()) { [void]$md.AppendLine(''); [void]$md.AppendLine($t.Trim()) }
}
$times = Read-Utf8 (Join-Path $Out 'probe-times.txt')
if ($times.Trim()) {
    [void]$md.AppendLine('')
    [void]$md.AppendLine('### Settings window open times inside the probe (ms; .first = first open, .again = after 3 s idle)')
    [void]$md.AppendLine('```')
    [void]$md.AppendLine($times.Trim())
    [void]$md.AppendLine('```')
}
[void]$md.AppendLine('')
[void]$md.AppendLine('Screenshots, logs and full results: download the artifact at the bottom of the run page.')
Write-Utf8 (Join-Path $Out 'SUMMARY.md') $md.ToString()
if ($env:GITHUB_STEP_SUMMARY) { [System.IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $md.ToString(), $Utf8) }
Write-Host $md.ToString()
if (-not $allOk) { exit 1 }
