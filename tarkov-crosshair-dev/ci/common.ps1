# W29 (v5.1) shared paths and helpers for the Windows CI scripts. Dot-source it: . "$PSScriptRoot/common.ps1"
# Repo layout (made by push-to-github.sh):
#   <repo>/tarkov-crosshair-v4/            app source (src/, app.ico, README files)
#   <repo>/tarkov-shade-module/src/        filter module source (the tests compare it with src/Shade)
#   <repo>/tarkov-crosshair-dev/ci/        these scripts
#   <repo>/tarkov-crosshair-dev/tests-v4/, tests-v42-filtertut/, tests-v3/fixtures/
#   <repo>/.github/workflows/windows-ci.yml
# Everything the CI writes goes to <repo>/ci-out/ (uploaded as the run's artifact) and <repo>/ci-work/ (scratch).
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$script:Ci   = $PSScriptRoot
$script:Dev  = Split-Path -Parent $script:Ci
$script:Repo = Split-Path -Parent $script:Dev
$script:App  = Join-Path $script:Repo 'tarkov-crosshair-v4'
$script:Src  = Join-Path $script:App 'src'
$script:Out  = Join-Path $script:Repo 'ci-out'
$script:Work = Join-Path $script:Repo 'ci-work'
# local check (ci/local-check.sh) points these at a scratch folder and at a source snapshot
if ($env:TARKOV_CI_OUT) { $script:Out = $env:TARKOV_CI_OUT }
if ($env:TARKOV_CI_WORK) { $script:Work = $env:TARKOV_CI_WORK }
if ($env:TARKOV_CI_APP) { $script:App = $env:TARKOV_CI_APP; $script:Src = Join-Path $script:App 'src' }
$script:Utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Force -Path $script:Out, $script:Work | Out-Null

function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, $script:Utf8)
}

function Read-Utf8([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

# One status file per CI step: ci-out/status-<step>.txt = "PASS <one line>" or "FAIL <one line>". summary.ps1 reads them.
function Set-Status([string]$Step, [bool]$Ok, [string]$Line) {
    $word = 'FAIL'
    if ($Ok) { $word = 'PASS' }
    Write-Utf8 (Join-Path $script:Out "status-$Step.txt") ("$word $Line")
    Write-Host "[$Step] $word $Line"
}

# Runs a program, waits up to $TimeoutSec, writes stdout+stderr to $Log (UTF-8). Returns the exit code (-1 = timed out).
function Invoke-Logged([string]$Exe, [string[]]$ArgList, [string]$Log, [int]$TimeoutSec = 1800, [string]$Cwd = $null) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    foreach ($a in $ArgList) { [void]$psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    if ($Cwd) { $psi.WorkingDirectory = $Cwd }
    $p = [System.Diagnostics.Process]::Start($psi)
    $so = $p.StandardOutput.ReadToEndAsync()
    $se = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        try { $p.Kill($true) } catch { }
        Write-Utf8 $Log ($so.Result + $se.Result + "`nTIMEOUT after $TimeoutSec s")
        return -1
    }
    $p.WaitForExit()
    Write-Utf8 $Log ($so.Result + $se.Result)
    return $p.ExitCode
}

function Get-Dotnet {
    $c = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    foreach ($p in @("$env:ProgramFiles\dotnet\dotnet.exe", "$HOME/.dotnet/dotnet")) { if ($p -and (Test-Path $p)) { return $p } }
    throw 'dotnet (the .NET 8 SDK) was not found'
}
