// 用途：分开处理侧身、跳跃、换手过渡和移动，保持每帧更新函数简短。
namespace TarkovCrosshair
{
    public sealed partial class MotionEngine
    {
        /// <summary>按原有切换或按住规则更新侧身方向。</summary>
        private void UpdateLean(InputState input, GlobalSettings g)
        {
            if (g.LeanMode == "toggle")
            {
                if (input.LeanL && !prev.LeanL) leanTarget = (leanTarget == -1 ? 0 : -1);
                if (input.LeanR && !prev.LeanR) leanTarget = (leanTarget == 1 ? 0 : 1);
            }
            else
            {
                if (input.LeanL && !input.LeanR) leanTarget = -1;
                else if (input.LeanR && !input.LeanL) leanTarget = 1;
                else leanTarget = 0;
            }
        }

        /// <summary>按下时启动跳跃，已有跳跃不重启；准星重新启用时重置跳跃。</summary>
        private void UpdateJump(InputState input, JumpSettings js, bool ringOnEdge, double dtMs)
        {
            if (ringOnEdge) { jumpOn = false; jumpT = 0; }
            bool jumpOk = js != null && js.Enabled && js.TotalMs > 0;
            if (!jumpOk) jumpOn = false;
            else
            {
                if (input.Jump && !prev.Jump && !jumpOn) { jumpOn = true; jumpT = 0; }
                if (jumpOn) { jumpT += dtMs; if (jumpT >= js.TotalMs) { jumpOn = false; jumpT = 0; } }
            }
            JumpOffset = jumpOn ? JumpPath.Offset(js, jumpT) : 0.0;
        }

        /// <summary>平滑切换左右手偏移，预览仍使用正在编辑的手。</summary>
        private void UpdateHandBlend(Profile p, GlobalSettings g, PreviewMode pm, bool preview, double dtMs)
        {
            int edit = g.EditHand == 1 ? 1 : 0;
            int want;
            if (!preview) want = liveSwitched ? 1 : 0;
            else if (pm == PreviewMode.HandFlip) want = ((previewMs % 2400.0) < 1200.0) ? edit : 1 - edit;
            else want = edit;
            handTarget = want;
            if (want != blendTo) { blendFrom = blend; blendTo = want; blendT = 0; }
            blendT += dtMs;
            if (p.HandSwitchMs <= 0 || blendT >= p.HandSwitchMs) blend = blendTo;
            else
            {
                double u = blendT / p.HandSwitchMs;
                blend = blendFrom + (blendTo - blendFrom) * (u * u * (3 - 2 * u));
            }
            p.FillView(0, view0);
            p.FillView(1, view1);
        }

        /// <summary>保持原有移动速度曲线，仅在该轴目标变化时重新计时。</summary>
        private void UpdateMovement(InputState input, Profile p, double dtMs)
        {
            int rawX = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
            int rawF = (input.Fwd ? 1 : 0) - (input.Back ? 1 : 0);
            int dir = DirFromSigns(rawF, rawX);
            int lt = (int)leanTarget;
            if (dir != prevDir || lt != prevLean)
            {
                for (int a = 0; a < 2; a++)
                {
                    // 仅当该轴目标改变时，从当前偏移重新开始计时。
                    if (AxisTarget(a, prevDir, prevLean) != AxisTarget(a, dir, lt))
                    {
                        from[a] = pos[a]; travelMs[a] = 0; moving[a] = true;
                    }
                }
                prevDir = dir; prevLean = lt;
            }
            curDir = dir;
            for (int a = 0; a < 2; a++)
            {
                MoveCurve c = a == 0 ? p.CurveAD : p.CurveWS;
                double total = c == null ? 0 : c.DurationMs;
                double target = AxisTarget(a, dir, lt);
                if (moving[a])
                {
                    travelMs[a] += dtMs;
                    if (total <= 0 || travelMs[a] >= total) { moving[a] = false; pos[a] = target; }
                    else pos[a] = from[a] + (target - from[a]) * MotionCurve.Progress(c.Speeds, travelMs[a] / total);
                }
                else pos[a] = target;
            }
        }
    }
}
