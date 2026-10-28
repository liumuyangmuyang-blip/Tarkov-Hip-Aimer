<!-- 用途：记录点一下切换开镜时，Tab 强制关镜导致瞄准点显隐反转的核实结果和待确认方案。 -->
# Tab 强制关镜后的瞄准点显隐反转

Status: ready-for-agent
Resolution: completed

核实日期：2026-10-28。

## 结论与证据边界

修复前源码确实会在用户描述的操作中产生显隐反转。真实源码的离线输入链路连续复现三次；目前已完成修复，结果见 [validation.md](./validation.md)。
游戏里“开镜时按 Tab 会强制关镜”沿用用户提供的行为，本次没有启动或操作游戏。
核实时没有正在运行的 TarkovHipAimer.exe；仓库 settings.json 保存的是 hold，复现明确使用 toggle，不能把磁盘配置当成用户的运行配置。

核实阶段只新增文档；收到“开始实施”后，已修改源码并重编 EXE，未修改玩家 settings.json。

## 修复前原因

以下行号对应修复前提交 `3ed0d4a168c620ec386cd9a9406c75852074671e`。

- `TarkovHipAimer/src/Overlay.cs:243-248`：采集输入，经实时输入过滤后更新引擎。
- `TarkovHipAimer/src/Motion.cs:191-200`：采集开镜等按键，没有采集 Tab 或 Esc。
- `TarkovHipAimer/src/Motion.cs:424-435`：点一下切换模式下，仅在开镜键由松开变为按下时翻转 `adsToggled`。
- `TarkovHipAimer/src/Motion.cs:467-478`：根据工具记录的开镜状态决定瞄准点是否可见。

游戏已经强制关镜，工具仍记录为开镜。之后再按右键，工具把记录改为未开镜，恰好与游戏相反。

## 最小复现

输入顺序是：空闲、右键开镜、松开、Tab 打开背包、松开、Tab 关闭背包、松开、再次右键开镜。
只断言关闭背包及再次开镜后的显隐，不预设背包里面是否应该显示瞄准点。

| 步骤 | 工具实际透明度 Alpha | 按开镜状态应有的结果 |
| --- | --- | --- |
| 未开镜 | 1，显示 | 显示 |
| 右键开镜 | 0，隐藏 | 隐藏 |
| Tab 强制关镜，再按 Tab 关闭背包 | 0，隐藏 | 显示 |
| 再次右键开镜 | 1，显示 | 隐藏 |

以下是核实阶段实际运行的命令。源码拆分后，当前回归入口为仓库根目录的 `pwsh -NoProfile -File tests/Run-AdsCancellationTests.ps1`；下面保留原始失败证据。

```powershell
$ErrorActionPreference = 'Stop'
# 在内存中编译实际输入和显示逻辑，不启动程序或发送真实按键。
Add-Type -Path @('TarkovHipAimer\src\Motion.cs', 'TarkovHipAimer\src\Settings.cs', 'TarkovHipAimer\src\MotionCurve.cs', 'TarkovHipAimer\src\InputResolver.cs')
$pressed = [System.Collections.Generic.HashSet[int]]::new()
$read = [System.Func[int,bool]]{ param([int]$vk) return $pressed.Contains($vk) }
$sequence = @(0, 2, 0, 9, 0, 9, 0, 2)
$failed = $false
foreach ($attempt in 1..3) {
    # 每轮重新创建引擎，关闭渐变，只检查退出背包及再次开镜后的显隐。
    $config = [TarkovCrosshair.GlobalSettings]::new()
    $profile = [TarkovCrosshair.Profile]::new()
    $config.AdsMode = 'toggle'
    $config.ShowFadeMs = $config.HideFadeMs = 0
    $config.OnlyWhenGameFocused = $false
    $engine = [TarkovCrosshair.MotionEngine]::new()
    $filter = [TarkovCrosshair.LiveInputFilter]::new()
    $alphas = [System.Collections.Generic.List[double]]::new()
    foreach ($key in $sequence) {
        $pressed.Clear()
        if ($key -ne 0) { [void]$pressed.Add($key) }
        $raw = [TarkovCrosshair.MotionEngine]::ReadInput($config.Keys, $read)
        $inputState = $filter.Next($raw, $config.Enabled, $true)
        $engine.Update(0.016, $inputState, $profile, $config, [TarkovCrosshair.PreviewMode]::Live, 1.0)
        $alphas.Add($engine.Alpha)
    }
    'Run {0}: Alpha={1}' -f $attempt, ($alphas -join ',')
    $closePass = $alphas[6] -eq 1
    $aimPass = $alphas[7] -eq 0
    'After inventory closes: actual={0}, expected=1, PASS={1}' -f $alphas[6], $closePass
    'After ADS opens again: actual={0}, expected=0, PASS={1}' -f $alphas[7], $aimPass
    if (-not ($closePass -and $aimPass)) { $failed = $true }
}
if ($failed) { exit 1 }
```

三轮均输出 `Alpha=1,0,0,0,0,0,0,1`。每轮的两项断言均失败，命令退出码为 1：

```text
After inventory closes: actual=0, expected=1, PASS=False
After ADS opens again: actual=1, expected=0, PASS=False
```

## 相邻行为

- 背包中右键物品也会被当作开镜输入，继续翻转记录，工具没有区分操作场景。
- Tab、Esc 均不产生当前 InputState 中的输入；对照测试中 A 键正常产生 Left。
- 离线模拟准星总开关关闭再开启后，`adsToggled` 仍为 true；不能把“关一下再开准星”作为本问题的恢复方法。
- “清掉工具记录的开镜状态”和“背包中是否隐藏瞄准点”是两个不同要求。

## 已确认方案

1. 增加明确的取消开镜输入。默认 Tab、Esc 被按下时，将点一下切换模式的记录设为未开镜，不做再次翻转。
2. 同一帧既有取消开镜又有开镜按键时，取消优先；连续按取消键不应把记录切回开镜。
3. 只调整开镜跟踪，不调用整个 MotionEngine.Reset，避免清掉换手、强化准星和移动状态。
4. 取消开镜键支持多个，默认 Tab 和 Esc，可以在设置中修改并保存；不把切窗口等其他操作一律当成关镜。
5. 不判断是否在背包中。默认取消键在允许读取实时按键时都是通用的清零操作，按住开镜方式保持原有行为。
6. 背包里任意次数右键后，用 Tab 或 Esc 退出，退出后的瞄准点应正确显示，下一次开镜应正确隐藏。
7. 本次不单独控制背包内的瞄准点显隐，也不承诺背包里忽略所有右键；通过退出时清零保证回到游戏后状态正确。

建议沿用当前按键查询方式，取消输入只增加少量按键查询和布尔判断，无需为这一局部修复增加高频截图或图像识别。

## 讨论结果与后续流程

- Q1：同时覆盖背包内右键和 Esc 退出。
- Q2：取消开镜键支持配置。
- Q3：只保证退出背包后的显隐正确，背包内显隐不单独控制。
- Q4：默认取消键为 Tab、Esc，不判断是否在背包内；取消优先，其他状态不重置；按住开镜不变。用户认可通过真实输入到显隐的链路验收，并合并为一个无前置依赖的完整修复任务。

需求说明见 [spec.md](./spec.md)，任务见 [01-fix-ads-cancellation.md](./issues/01-fix-ads-cancellation.md)。
to-spec、to-tickets、implement-spec 已完成；用户已于 2026-10-28 明确确认“开始实施”。
本次是容易调整的局部规则，不需要单独创建 ADR。
