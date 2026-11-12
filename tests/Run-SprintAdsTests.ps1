# 用途：以内存编译运行奔跑取消开镜回归，不启动窗口或修改玩家设置。
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
# 只编译纯计算源码，Motion 的拆分文件会自动加入。
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $repoPath 'TarkovHipAimer\src') -Filter '*.cs' |
    Where-Object { $_.BaseName -in @('Settings', 'InputResolver') -or $_.BaseName -like 'Motion*' } |
    ForEach-Object { $_.FullName })
Add-Type -Path ($sourcePaths + @((Join-Path $PSScriptRoot 'SprintAdsTests.cs')))
'PASS：奔跑与 v3.2 开镜兼容 {0} 项' -f [SprintAdsTests]::Run()
