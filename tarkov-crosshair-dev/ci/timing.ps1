# W29 (v5.1) CI step 3: start the real 准星 TarkovHipAimer.exe the way a user does (package folder from build.ps1) and time
# it. Run 1 is a first start (no 「数据 Data」 folder), runs 2..N are normal starts. For each run:
#   startup   the app's own 启动耗时 lines in crash.log (ms since the process started, per phase, up to 启动完成)
#   settings  a second start of the exe asks the running one to show its settings window (the app's single-instance
#             signal); time until a new visible window of the app appears, plus the app's 设置页 build lines
#   screenshots of the desktop after startup and after the settings window opened (ci-out/shots)
# The app is then stopped (Stop-Process). No game is running on the CI machine, so the crosshair has nothing to follow.
param([int]$Runs = 3, [int]$StartTimeoutSec = 60)
. "$PSScriptRoot/common.ps1"

if (-not $IsWindows) { Set-Status 'timing' $false 'the startup timer needs Windows'; exit 1 }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class CiWin
{
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    public static string[] Visible(int pid)
    {
        List<string> list = new List<string>();
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid && IsWindowVisible(h))
            {
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(h, sb, 256);
                if (sb.Length > 0) list.Add(sb.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }
}
'@

function Save-Screen([string]$Name) {
    try {
        $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
        $g.Dispose()
        $dir = Join-Path $Out 'shots'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $bmp.Save((Join-Path $dir "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
    catch { Write-Host "screenshot $Name failed: $($_.Exception.Message)" }
}

function Find-Log([string]$RunDir) {
    $c = @(Get-ChildItem -Path $RunDir -Recurse -Filter 'crash.log' -File -ErrorAction SilentlyContinue)
    foreach ($d in @("$env:LOCALAPPDATA\TarkovHipAimer", "$env:TEMP\TarkovHipAimer")) {
        if ($d -and (Test-Path $d)) { $c += @(Get-ChildItem -Path $d -Recurse -Filter 'crash.log' -File -ErrorAction SilentlyContinue) }
    }
    if ($c.Count -eq 0) { return $null }
    return ($c | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
}

$pkg = Join-Path $Out 'app/TarkovHipAimer'
$runDir = Join-Path $Work 'run/TarkovHipAimer'
if (Test-Path $runDir) { Remove-Item -LiteralPath $runDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
Copy-Item -Path (Join-Path $pkg '*') -Destination $runDir -Recurse -Force
$exe = Join-Path $runDir '准星 TarkovHipAimer.exe'
if (-not (Test-Path -LiteralPath $exe)) { Set-Status 'timing' $false 'exe missing (build step failed)'; exit 1 }

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine('### Startup and settings window (real exe, runner at 100 %)')
[void]$md.AppendLine('')
[void]$md.AppendLine('| run | startup done (app clock) | settings window shown after the 2nd start | page builds logged |')
[void]$md.AppendLine('|---|---:|---:|---:|')
$all = New-Object System.Text.StringBuilder
$okRuns = 0
for ($i = 1; $i -le $Runs; $i++) {
    $kind = 'normal start'
    if ($i -eq 1) { $kind = 'first start' }
    $log = Find-Log $runDir
    $before = 0
    if ($log) { $before = (Read-Utf8 $log).Length }
    $p = $null
    $doneMs = ''; $showMs = ''; $pages = 0; $note = ''
    try {
        $p = Start-Process -FilePath $exe -WorkingDirectory $runDir -PassThru
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $text = ''
        while ($sw.Elapsed.TotalSeconds -lt $StartTimeoutSec) {
            Start-Sleep -Milliseconds 100
            $log = Find-Log $runDir
            if ($log) {
                $t = Read-Utf8 $log
                if ($t.Length -ge $before) { $text = $t.Substring([Math]::Min($before, $t.Length)) } else { $text = $t }
                if ($text -match '启动耗时 启动完成[^:]*: (\d+) ms') { $doneMs = $Matches[1]; break }
            }
            if ($p.HasExited) { $note = "exited with code $($p.ExitCode)"; break }
        }
        if ($doneMs) { $okRuns++ } elseif (-not $note) { $note = "no 启动完成 line within $StartTimeoutSec s" }
        Start-Sleep -Seconds 2
        Save-Screen ("startup-run$i")
        if (-not $p.HasExited) {
            $vis0 = @([CiWin]::Visible($p.Id))
            $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
            $second = Start-Process -FilePath $exe -WorkingDirectory $runDir -PassThru
            while ($sw2.Elapsed.TotalSeconds -lt 15) {
                $vis = @([CiWin]::Visible($p.Id))
                if ($vis.Count -gt $vis0.Count) { $showMs = [string][int]$sw2.Elapsed.TotalMilliseconds; break }
                Start-Sleep -Milliseconds 20
            }
            if (-not $showMs) { $note += " no new window after the 2nd start (visible before: $($vis0 -join ' / '))" }
            try { $second.WaitForExit(5000) | Out-Null } catch { }
            Start-Sleep -Seconds 3
            Save-Screen ("settings-run$i")
            $log = Find-Log $runDir
            if ($log) { $t = Read-Utf8 $log; $text = $t.Substring([Math]::Min($before, $t.Length)) }
        }
        $lines = @($text -split "`n" | Where-Object { $_ -match '启动耗时|界面卡住|界面恢复' } | ForEach-Object { $_.TrimEnd("`r") })
        $pages = @($lines | Where-Object { $_ -match '设置页' }).Count
        [void]$all.AppendLine("== run $i ($kind)$(if ($note) { ' NOTE: ' + $note.Trim() })")
        foreach ($l in $lines) { [void]$all.AppendLine($l) }
    }
    catch { $note += ' ' + $_.Exception.Message }
    finally {
        if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    $d1 = '-'; if ($doneMs) { $d1 = "$doneMs ms" }
    $d2 = '-'; if ($showMs) { $d2 = "$showMs ms" }
    [void]$md.AppendLine("| $i ($kind) | $d1 | $d2 | $pages$(if ($note) { ' (' + $note.Trim() + ')' }) |")
}
[void]$md.AppendLine('')
[void]$md.AppendLine('All 启动耗时 lines: timing.txt in the artifact. Settings-window time is measured from outside (window appears), so it')
[void]$md.AppendLine('includes about 20 ms of polling. Per-page open times at each scale: probe-times.txt (probe mode "timing").')
Write-Utf8 (Join-Path $Out 'timing.txt') $all.ToString()
Write-Utf8 (Join-Path $Out 'timing-summary.md') $md.ToString()
Set-Status 'timing' ($okRuns -eq $Runs) "$okRuns of $Runs starts reached 启动完成"
if ($okRuns -ne $Runs) { exit 1 }
