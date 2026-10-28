# 用途：用实际输入和显隐计算回归取消开镜行为，不发送真实按键或启动游戏。
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
# 仅编译不依赖窗口的源码，拆分后的同类文件也会自动加入。
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $repoPath 'TarkovHipAimer\src') -Filter '*.cs' |
    Where-Object { $_.BaseName -in @('Settings', 'KeyBindings', 'InputResolver', 'AdsInput') -or $_.BaseName -like 'Motion*' } |
    ForEach-Object { $_.FullName })
Add-Type -Path ($sourcePaths + @(
    (Join-Path $PSScriptRoot 'AdsCancellationTests.cs'),
    (Join-Path $PSScriptRoot 'SettingsCancellationTests.cs')))
$pressed = [System.Collections.Generic.HashSet[int]]::new()
$readKey = [System.Func[int,bool]]{ param([int]$vk) return $pressed.Contains($vk) }
$config = [TarkovCrosshair.GlobalSettings]::new()
$config.AdsMode = 'toggle'
$config.OnlyWhenGameFocused = $false
$config.ShowFadeMs = $config.HideFadeMs = 0
$profile = [TarkovCrosshair.Profile]::new()
$engine = [TarkovCrosshair.MotionEngine]::new()
$filter = [TarkovCrosshair.LiveInputFilter]::new()
$alphas = [System.Collections.Generic.List[double]]::new()
# 包含打开和关闭背包，避免预设背包里的瞄准点是否应该显示。
foreach ($key in @(0, 2, 0, 9, 0, 9, 0, 2)) {
    $pressed.Clear()
    if ($key -ne 0) { [void]$pressed.Add($key) }
    $raw = [TarkovCrosshair.MotionEngine]::ReadInput($config.Keys, $readKey)
    $inputState = $filter.Next($raw, $config.Enabled, $true)
    $engine.Update(0.016, $inputState, $profile, $config, [TarkovCrosshair.PreviewMode]::Live, 1.0)
    $alphas.Add($engine.Alpha)
}
if ($alphas[6] -ne 1 -or $alphas[7] -ne 0) {
    throw ('退出背包后应显示、再次开镜应隐藏；实际 Alpha={0},{1}' -f $alphas[6], $alphas[7])
}
'PASS：原始 Tab 显隐反转场景'
$inputChecks = [AdsCancellationTests]::Run()
$settingsChecks = [SettingsCancellationTests]::Run()
'PASS：输入到显隐 {0} 项，配置保存与兼容 {1} 项' -f $inputChecks, $settingsChecks
