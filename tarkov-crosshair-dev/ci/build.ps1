# W29 (v5.1) CI step 1: compile 准星 TarkovHipAimer.exe exactly like package-v502.sh (Roslyn csc from the .NET SDK, C# 5,
# .NET Framework 4.8 reference assemblies, x64, optimize, deterministic, warn:4, app.manifest + app.ico), require
# 0 errors and 0 warnings, then build the CI-only helpers: the app as a library + the layout probe (RenderDriver.exe)
# and DpiTool.exe. Also runs on Linux with pwsh (local check: ci/local-check.sh).
. "$PSScriptRoot/common.ps1"

$dotnet = Get-Dotnet
$sdkRoot = Join-Path (Split-Path -Parent $dotnet) 'sdk'
$csc = Get-ChildItem -Path $sdkRoot -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'Roslyn/bincore/csc.dll') } |
    Sort-Object { [version](($_.Name -split '-')[0]) } | Select-Object -Last 1
if (-not $csc) { Set-Status 'build' $false "no Roslyn csc.dll under $sdkRoot"; exit 1 }
$csc = Join-Path $csc.FullName 'Roslyn/bincore/csc.dll'

# .NET Framework 4.8 reference assemblies: the same NuGet package as the local build (1.0.3)
$refs = Join-Path $HOME '.nuget/packages/microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8'
if (-not (Test-Path (Join-Path $refs 'mscorlib.dll'))) {
    $pkgDir = Join-Path $Work 'refs'
    $refs = Join-Path $pkgDir 'build/.NETFramework/v4.8'
    if (-not (Test-Path (Join-Path $refs 'mscorlib.dll'))) {
        $zip = Join-Path $Work 'refs.zip'
        $url = 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg'
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        Expand-Archive -Path $zip -DestinationPath $pkgDir -Force
    }
}
$refList = 'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll' |
    ForEach-Object { '-r:"' + (Join-Path $refs $_) + '"' }
$common = @('-noconfig', '-nostdlib+', '-nologo', '-utf8output', '-langversion:5', '-codepage:65001', '-platform:x64', '-optimize+', '-deterministic', '-warn:4')

# Compiles with a response file (paths may hold spaces / Chinese). Returns @(exitCode, errors, warnings, log).
function Invoke-Csc([string]$Name, [string[]]$Opts, [string[]]$Files) {
    $rsp = Join-Path $Work "$Name.rsp"
    $lines = @($common) + $Opts + $refList + ($Files | ForEach-Object { '"' + $_ + '"' })
    Write-Utf8 $rsp ([string]::Join("`n", $lines))
    $log = Join-Path $Out "build-$Name.log"
    $rc = Invoke-Logged $dotnet @('exec', $csc, "@$rsp") $log 900
    $text = Read-Utf8 $log
    $err = ([regex]::Matches($text, ': error [A-Z]+\d+')).Count
    $warn = ([regex]::Matches($text, ': warning [A-Z]+\d+')).Count
    return @($rc, $err, $warn, $log)
}

$srcFiles = @(Get-ChildItem -Path $Src -Filter '*.cs' -File) + @(Get-ChildItem -Path (Join-Path $Src 'Shade') -Filter '*.cs' -File -ErrorAction SilentlyContinue)
$srcFiles = @($srcFiles | ForEach-Object { $_.FullName } | Sort-Object)
$buildDir = Join-Path $Out 'build'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$exe = Join-Path $buildDir 'TarkovHipAimer.exe'
$r = Invoke-Csc 'exe' @('-target:winexe', ('-win32manifest:"' + (Join-Path $Src 'app.manifest') + '"'), ('-win32icon:"' + (Join-Path $App 'app.ico') + '"'), ('-out:"' + $exe + '"')) $srcFiles
$ok = ($r[0] -eq 0) -and ($r[1] -eq 0) -and ($r[2] -eq 0) -and (Test-Path $exe)
$detail = "exe: exit $($r[0]), $($r[1]) errors, $($r[2]) warnings, $($srcFiles.Count) files"
if ($ok) { $detail += ', ' + (Get-Item $exe).Length + ' bytes' }
else {
    $first = (Read-Utf8 $r[3]) -split "`n" | Where-Object { $_ -match ': (error|warning) ' } | Select-Object -First 15
    Write-Host ($first -join "`n")
}

# package-like folder so the startup timer runs the exe the way a user does (exe name from package-v502.sh)
if (Test-Path $exe) {
    $pkg = Join-Path $Out 'app/TarkovHipAimer'
    New-Item -ItemType Directory -Force -Path $pkg | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $pkg '准星 TarkovHipAimer.exe') -Force
    $readme = Join-Path $App '说明 README.txt'
    if (Test-Path -LiteralPath $readme) { Copy-Item -LiteralPath $readme -Destination $pkg -Force }
}

# CI-only helpers (their warnings do not count): app as a library, the layout probe, DpiTool
$probeDir = Join-Path $Work 'probe'
New-Item -ItemType Directory -Force -Path $probeDir | Out-Null
$lib = Invoke-Csc 'lib' @('-target:library', ('-out:"' + (Join-Path $probeDir 'TarkovHipAimer.dll') + '"')) $srcFiles
$manifest = (Read-Utf8 (Join-Path $Src 'app.manifest')) -replace 'requireAdministrator', 'asInvoker'
$probeManifest = Join-Path $probeDir 'probe.manifest'
Write-Utf8 $probeManifest $manifest
$probe = Invoke-Csc 'probe' @('-target:exe', ('-win32manifest:"' + $probeManifest + '"'), ('-r:"' + (Join-Path $probeDir 'TarkovHipAimer.dll') + '"'), ('-out:"' + (Join-Path $probeDir 'RenderDriver.exe') + '"')) @((Join-Path $Ci 'probe/UiCProbe.cs'))
Copy-Item (Join-Path $Ci 'probe/dialogs.tsv') $probeDir -Force
$readme = Join-Path $App '说明 README.txt'
if (Test-Path -LiteralPath $readme) { Copy-Item -LiteralPath $readme -Destination $probeDir -Force }
$toolDir = Join-Path $Work 'tools'
New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
$dpi = Invoke-Csc 'dpitool' @('-target:exe', ('-out:"' + (Join-Path $toolDir 'DpiTool.exe') + '"')) @((Join-Path $Ci 'tools/DpiTool.cs'))
$helpersOk = ($lib[0] -eq 0) -and ($probe[0] -eq 0) -and ($dpi[0] -eq 0)
$detail += "; probe helpers: library exit $($lib[0]), probe exit $($probe[0]), DpiTool exit $($dpi[0])"
Write-Utf8 (Join-Path $Out 'helpers-ok.txt') ([string]$helpersOk)
Set-Status 'build' ($ok -and $helpersOk) $detail
if (-not ($ok -and $helpersOk)) { exit 1 }
