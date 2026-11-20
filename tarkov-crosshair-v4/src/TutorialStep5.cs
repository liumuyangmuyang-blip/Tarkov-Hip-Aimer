// TutorialStep5.cs -- 首次设置教程 script step 5 (去靶场). System.Drawing only (no WinForms), C# 5, no per-frame allocation.
// Owner: step worker 5. Scene: a top-down range map (the player walks from the spawn to the 10 m line in front of the
// wall), a first-person mini view (the wall grows while walking) and a 3-item checklist that ticks as the demo runs.
// Every distance except the 10 m stand-off is 示意. TutorialStep5Math (top) is pure (System only) so tests-v4 can compile
// it alone: define TUTORIAL_MATH_ONLY to leave out the drawing part.
using System;
using System.Collections.Generic;
#if !TUTORIAL_MATH_ONLY
using System.Drawing;
using System.Drawing.Drawing2D;
#endif

namespace TarkovCrosshair
{
    /// <summary>Pure timeline and geometry of step 5 (all 示意 except StandOffM = 10 m from the script).</summary>
    public static class TutorialStep5Math
    {
        /// <summary>The distance to the wall the script asks for.</summary>
        public const double StandOffM = 10.0;
        /// <summary>示意: where the walk starts (metres from the wall).</summary>
        public const double StartM = 20.0;
        /// <summary>Metres shown on the map, wall at 0.</summary>
        public const double MapM = 22.0;
        /// <summary>示意 stride length (one footprint per stride).</summary>
        public const double StrideM = 0.8;

        // timeline (ms since the demo started)
        public const double BootMs = 900;       // map grid scans in
        public const double WalkStartMs = 2300; // after items 1 and 2 ticked
        public const double WalkEndMs = 6100;   // arrives at 10 m
        /// <summary>Times at which the checklist items tick: 进靶场 / 装备+武器 / 墙 10 米 (= arrival).</summary>
        public static readonly double[] TickMs = new double[] { 1100, 1900, WalkEndMs };
        public const int ItemCount = 3;

        /// <summary>Boot reveal 0..1 (grid scan line position).</summary>
        public static double Boot01(double tMs)
        {
            if (!(tMs > 0)) return 0;
            return tMs >= BootMs ? 1 : tMs / BootMs;
        }

        /// <summary>Walk progress 0..1 (ease in-out).</summary>
        public static double Walk01(double tMs)
        {
            if (!(tMs > WalkStartMs)) return 0;
            if (tMs >= WalkEndMs) return 1;
            double u = (tMs - WalkStartMs) / (WalkEndMs - WalkStartMs);
            return u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
        }

        /// <summary>Player distance to the wall (m) at tMs: StartM, walking to exactly StandOffM.</summary>
        public static double DistanceM(double tMs)
        {
            double w = Walk01(tMs);
            if (w >= 1) return StandOffM;
            return StartM + (StandOffM - StartM) * w;
        }

        public static bool Arrived(double tMs) { return tMs >= WalkEndMs; }

        public static bool Walking(double tMs) { return tMs > WalkStartMs && tMs < WalkEndMs; }

        /// <summary>Checklist item i ticked at tMs.</summary>
        public static bool Ticked(int i, double tMs)
        {
            if (i < 0 || i >= ItemCount) return false;
            return tMs >= TickMs[i];
        }

        /// <summary>0..1 fade-in of the tick of item i (200 ms).</summary>
        public static double Tick01(int i, double tMs)
        {
            if (i < 0 || i >= ItemCount || !(tMs >= TickMs[i])) return 0;
            double u = (tMs - TickMs[i]) / 200.0;
            return u >= 1 ? 1 : u;
        }

        /// <summary>Number of footprints left so far (one per stride walked).</summary>
        public static int Footprints(double tMs)
        {
            double walked = StartM - DistanceM(tMs);
            if (!(walked > 0)) return 0;
            return (int)Math.Floor(walked / StrideM + 1e-9);
        }

        /// <summary>Distance to the wall of footprint i (i = 0 is the first step after the start).</summary>
        public static double FootprintM(int i) { return StartM - (i + 1) * StrideM; }

        /// <summary>Head bob (-1..1) while walking, 0 standing (two bobs per stride pair).</summary>
        public static double Bob(double tMs)
        {
            if (!Walking(tMs)) return 0;
            double walked = StartM - DistanceM(tMs);
            return Math.Sin(walked / StrideM * Math.PI);
        }

        /// <summary>Map y (pixels) of a distance from the wall, wall at top, MapM at bottom.</summary>
        public static double MapY(double distM, double top, double bottom)
        {
            if (double.IsNaN(distM)) distM = 0;
            if (distM < 0) distM = 0;
            if (distM > MapM) distM = MapM;
            return top + (bottom - top) * distM / MapM;
        }

        /// <summary>First-person screen height (px) of something sizeM tall at distM, focal f px per m at 1 m.</summary>
        public static double ProjectPx(double sizeM, double distM, double focalPx)
        {
            if (!(distM > 0.05)) distM = 0.05;
            return focalPx * sizeM / distM;
        }

        /// <summary>Index into the 0.1 m label table (0 .. maxTenths).</summary>
        public static int TenthsIndex(double distM, int maxTenths)
        {
            if (double.IsNaN(distM) || distM < 0) return 0;
            if (distM * 10 >= maxTenths) return maxTenths;
            int i = (int)Math.Round(distM * 10, MidpointRounding.AwayFromZero);
            if (i > maxTenths) i = maxTenths;
            return i;
        }

        /// <summary>Expanding pulse 0..1 every periodMs after arrival (the ring around the player), -1 = none.</summary>
        public static double Pulse01(double tMs, double periodMs)
        {
            if (!(tMs >= WalkEndMs) || !(periodMs > 0)) return -1;
            return ((tMs - WalkEndMs) % periodMs) / periodMs;
        }
    }

#if !TUTORIAL_MATH_ONLY
    public static class TutorialStep5
    {
        /// <summary>Appends this script step's page(s) to the wizard, in order.</summary>
        public static void Add(List<TutorialStep> steps)
        {
            steps.Add(new TutorialStep5Range());
        }
    }

    /// <summary>Script step 5: 去靶场 (go to the range, gear up, stand 10 m from a clean wall).</summary>
    internal sealed class TutorialStep5Range : TutorialStep
    {
        private const int ChipReplay = 1;
        private const int MaxTenths = 300;
        private static readonly string[] DistLabel = new string[MaxTenths + 1];   // "距离 12.3 米"
        private static readonly string[] DistShort = new string[MaxTenths + 1];   // "12.3 米"
        private static readonly string[] Items = new string[] { "进靶场或练习模式", "常穿的装备 · 最常用的枪", "干净的墙 · 站在 10 米" };
        private static readonly string[] RulerLabel = new string[] { "0", "5", "10", "15", "20" };

        private static readonly string[] Order = new string[] { "先搞定后坐力（上抬偏移量）", "再调移动偏移：先按住 D", "然后 A · W · S" };
        private static readonly string[] OrderNum = new string[] { "1", "2", "3" };
        private static readonly TutorialField[] RiseFields = new TutorialField[]
        {
            TutorialField.Offset("上抬偏移量", "推荐改", true, OffsetAxis.Y, 0, 1000,
                delegate(Profile p) { return p.RecoilMaxPx; }, delegate(Profile p, double v) { p.RecoilMaxPx = v; }),
        };

        private double t;     // demo clock (ms)
        private bool arrivePushed;
        private bool riseSet; // the user changed 上抬偏移量 on this page
        // cached layout widths (re-measured only when the font object changes, e.g. on a window resize)
        private Font measuredFont;
        private float distLabelW, mapTitleW, statusW;

        static TutorialStep5Range()
        {
            for (int i = 0; i <= MaxTenths; i++)
            {
                string v = (i / 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
                    (i % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
                // v4.1 (DOCCHECK41 T6): 「米」 like every other distance in the tutorial (wall tag 「10 米」, terminal 「距离：10 米」)
                DistShort[i] = v + " 米";
                DistLabel[i] = "距离 " + v + " 米";
            }
        }

        public TutorialStep5Range()
        {
            Id = "range"; Chapter = "实战校准"; Title = "去靶场";
            Body = "进靶场或练习模式。\n穿上常穿的装备，拿最常用的枪。\n找一面干净的墙，站在 10 米处。";
            DevNote = "接下来到游戏里调。顺序很重要：\n1. 先搞定后坐力：站定连续开枪，调好上抬偏移量（第 3 步讲过）。\n2. 再调移动偏移：先单独按住 D，然后 A、W、S。";
            Hint = "准备好了，点「下一步」";
            Prompts = new string[] { "站定连续开枪，量出上跳，填进「上抬偏移量」", "后坐力搞定了？点「下一步」，开始调移动偏移" };
            Terminal = new string[] { "> 装备：常用  武器：常用" };
            Fields = RiseFields;
            FieldNote = "当前：{当前上抬}\n先搞定后坐力，再调移动偏移。";
            SettingsPage = TutorialPages.Recoil;
            ClickFires = false;
            KeysShown = 0;
        }

        public override void Enter(TutorialContext c)
        {
            t = 0; arrivePushed = false;
            PromptIndex = riseSet ? 1 : 0;
            if (c.Revisit) Done = true;
        }

        public override void Update(TutorialContext c, double dtMs)
        {
            if (double.IsNaN(dtMs) || dtMs < 0) dtMs = 0;
            t += dtMs;
            if (t > 1e9) t = TutorialStep5Math.WalkEndMs + 10000;
            LitKeys = TutorialStep5Math.Walking(t) ? TKey.W : 0;
            // the only task here is to read and click 下一步: count the step as done once the first item ticked
            if (TutorialStep5Math.Ticked(0, t)) Done = true;
            if (TutorialStep5Math.Arrived(t) && !arrivePushed)
            {
                arrivePushed = true;
                c.Term.Push("> 距离：10 米");
                c.Term.Push("> 顺序：先搞定后坐力 → 再调移动偏移");
            }
        }

        public override void OnFieldChanged(TutorialContext c)
        {
            riseSet = true;
            PromptIndex = 1;
        }

        public override void OnChip(TutorialContext c, int id)
        {
            if (id == ChipReplay) { t = 0; arrivePushed = true; }
        }

        // ------------------------------------------------------------------ drawing

        public override void Draw(TutorialGfx gx, TutorialContext c, RectangleF scene)
        {
            Graphics g = gx.G;
            gx.SceneBackground(scene);
            float pad = gx.S(10);
            RectangleF inner = new RectangleF(scene.X + pad, scene.Y + pad, scene.Width - 2 * pad, scene.Height - 2 * pad);
            RectangleF map, fp, list;
            if (c.Compact)
            {
                // v4.1: small scene = the core picture only (the walk to the 10 m wall); the two side panels overlapped
                DrawMap(gx, inner);
                g.ResetClip();
                return;
            }
            if (inner.Width >= inner.Height * 1.25f)
            {
                float mapW = (float)Math.Floor(inner.Width * 0.54f);
                map = new RectangleF(inner.X, inner.Y, mapW, inner.Height);
                float rx = map.Right + pad, rw = inner.Right - rx;
                float listH = Math.Min(gx.S(230), inner.Height * 0.5f);
                float fpH = inner.Height - listH - pad;
                fp = new RectangleF(rx, inner.Y, rw, fpH);
                list = new RectangleF(rx, fp.Bottom + pad, rw, inner.Bottom - fp.Bottom - pad);
            }
            else
            {
                float mapH = (float)Math.Floor(inner.Height * 0.56f);
                map = new RectangleF(inner.X, inner.Y, inner.Width, mapH);
                float by = map.Bottom + pad, bh = inner.Bottom - by, half = (inner.Width - pad) / 2f;
                fp = new RectangleF(inner.X, by, half, bh);
                list = new RectangleF(inner.X + half + pad, by, half, bh);
            }
            DrawMap(gx, map);
            DrawFirstPerson(gx, fp);
            DrawChecklist(gx, list);
            g.ResetClip();
        }

        private void Panel(TutorialGfx gx, RectangleF r, string title)
        {
            Graphics g = gx.G;
            gx.RegisterView(r, false);   // each panel is its own view (tag budget: max 2 per view)
            g.FillRectangle(gx.B(TutorialGfx.A(gx.Pal.Panel, 235)), r);
            g.DrawRectangle(gx.P(TutorialGfx.A(gx.Pal.Neon, 70), 1f), r.X, r.Y, r.Width, r.Height);
            gx.HudFrame(r, TutorialGfx.A(gx.Pal.Neon, 200), 10);
            if (title != null) gx.Text(title, gx.F.MonoSmall, TutorialGfx.A(gx.Pal.Neon, 210), r.X + gx.S(7), r.Y + gx.S(5), 0);
        }

        private void DrawMap(TutorialGfx gx, RectangleF r)
        {
            Graphics g = gx.G;
            TutorialPalette pal = gx.Pal;
            Measure(gx);
            Panel(gx, r, MapTitle);
            double boot = TutorialStep5Math.Boot01(t);
            bool arrived = TutorialStep5Math.Arrived(t);
            float top = r.Y + gx.S(46), bottom = r.Bottom - gx.S(22);
            float ruler = r.X + gx.S(30);
            float cx = r.X + (r.Width + gx.S(30)) / 2f;
            float pxPerM = (bottom - top) / (float)TutorialStep5Math.MapM;
            float revealY = (float)(top + (bottom - top) * boot);

            // floor grid (1 m), revealed by the boot scan line
            Pen grid = gx.P(TutorialGfx.A(pal.Neon, 18), 1f);
            for (int m = 0; m <= (int)TutorialStep5Math.MapM; m++)
            {
                float y = (float)TutorialStep5Math.MapY(m, top, bottom);
                if (y > revealY) break;
                bool major = m % 5 == 0;
                g.DrawLine(major ? gx.P(TutorialGfx.A(pal.Neon, 40), 1f) : grid, ruler, y, r.Right - gx.S(10), y);
                if (major) gx.TextMid(RulerLabel[m / 5], gx.F.MonoSmall, TutorialGfx.A(pal.Muted, 220), ruler - gx.S(5), y, 2);
            }
            int cols = (int)((r.Right - gx.S(10) - ruler) / pxPerM);
            for (int i = 0; i <= cols; i++)
            {
                float x = ruler + i * pxPerM;
                g.DrawLine(grid, x, top, x, Math.Min(revealY, bottom));
            }
            g.DrawLine(gx.P(TutorialGfx.A(pal.Neon, 90), 1f), ruler, top, ruler, Math.Min(revealY, bottom));
            gx.Text("米", gx.F.MonoSmall, TutorialGfx.A(pal.Muted, 200), ruler - gx.S(5), bottom + gx.S(4), 2);
            if (boot < 1)
            {
                gx.GlowLine(pal.Neon, 1.2f, r.X + gx.S(4), revealY, r.Right - gx.S(4), revealY);
                return;
            }

            // the wall (top)
            float wallHalf = pxPerM * 2.2f, wallY = top;
            float wallX1 = cx - wallHalf, wallX2 = cx + wallHalf;
            g.FillRectangle(gx.B(TutorialGfx.A(pal.Accent, 60)), wallX1, wallY - gx.S(7), 2 * wallHalf, gx.S(7));
            gx.GlowLine(pal.Accent, 2.2f, wallX1, wallY, wallX2, wallY);
            gx.Tag("墙靶", cx, r.Y + gx.S(22), pal.Accent, 1);

            // 10 m line
            float lineY = (float)TutorialStep5Math.MapY(TutorialStep5Math.StandOffM, top, bottom);
            float lx1 = ruler + gx.S(2), lx2 = r.Right - gx.S(12);
            if (arrived)
            {
                double pulse = 0.5 + 0.5 * Math.Sin((t - TutorialStep5Math.WalkEndMs) / 260.0);
                gx.GlowLine(TutorialGfx.A(pal.Neon, 150 + (int)(100 * pulse)), 1.8f, lx1, lineY, lx2, lineY);
            }
            else g.DrawLine(gx.Dashed(TutorialGfx.A(pal.Neon, 110), 1.2f), lx1, lineY, lx2, lineY);
            // tag on the LEFT end of the line (the distance bracket rides on the right, so the two never cross)
            gx.Tag("10 米 · 墙靶", lx1 + gx.S(4), lineY + gx.S(5), arrived ? pal.Neon : TutorialGfx.A(pal.Neon, 150), 0);

            // spawn marker
            float spawnY = (float)TutorialStep5Math.MapY(TutorialStep5Math.StartM, top, bottom);
            g.DrawEllipse(gx.Dashed(TutorialGfx.A(pal.Muted, 140), 1f), cx - gx.S(9), spawnY - gx.S(9), gx.S(18), gx.S(18));

            // footprints
            int n = TutorialStep5Math.Footprints(t);
            for (int i = 0; i < n; i++)
            {
                float fy = (float)TutorialStep5Math.MapY(TutorialStep5Math.FootprintM(i), top, bottom);
                float fx = cx + ((i & 1) == 0 ? -gx.S(4) : gx.S(4));
                double age = (n - 1 - i) / 10.0;
                int a = (int)(160 * Math.Max(0.15, 1 - age));
                gx.Dot(TutorialGfx.A(pal.Cyan, a), fx, fy, gx.S(1.6f));
            }

            // player: field of view toward the wall, marker, distance bracket
            double d = TutorialStep5Math.DistanceM(t);
            float py = (float)TutorialStep5Math.MapY(d, top, bottom);
            PointF[] fov = gx.Pts(3);
            fov[0] = new PointF(cx, py); fov[1] = new PointF(cx - wallHalf * 0.9f, wallY + gx.S(2)); fov[2] = new PointF(cx + wallHalf * 0.9f, wallY + gx.S(2));
            g.FillPolygon(gx.B(TutorialGfx.A(pal.Cyan, 22)), fov);
            g.DrawLine(gx.P(TutorialGfx.A(pal.Cyan, 70), 1f), fov[0], fov[1]);
            g.DrawLine(gx.P(TutorialGfx.A(pal.Cyan, 70), 1f), fov[0], fov[2]);
            double pulse01 = TutorialStep5Math.Pulse01(t, 1600);
            if (pulse01 >= 0)
            {
                float pr = gx.S(8) + gx.S(26) * (float)pulse01;
                g.DrawEllipse(gx.P(TutorialGfx.A(pal.Neon, (int)(170 * (1 - pulse01))), 1.2f), cx - pr, py - pr, 2 * pr, 2 * pr);
            }
            PointF[] tri = gx.Pts(4);
            float s = gx.S(8);
            tri[0] = new PointF(cx, py - s * 1.3f); tri[1] = new PointF(cx + s, py + s); tri[2] = new PointF(cx, py + s * 0.45f); tri[3] = new PointF(cx - s, py + s);
            Color pc = arrived ? pal.Neon : pal.Cyan;
            g.FillPolygon(gx.B(TutorialGfx.A(pc, 60)), tri);
            g.DrawPolygon(gx.P(pc, 1.5f), tri);
            gx.Text("你", gx.F.Tag, pc, cx + s + gx.S(5), py + gx.S(4), 0);

            float bx = cx + wallHalf + gx.S(16);
            float bxMax = r.Right - gx.S(10) - distLabelW - gx.S(8);
            if (bx > bxMax) bx = bxMax;
            if (bx < cx + gx.S(14)) bx = cx + gx.S(14);
            gx.Bracket(bx, wallY + gx.S(2), py, arrived ? pal.Neon : pal.Cyan, DistLabel[ShownTenths(d, arrived)]);

            // status (only when it fits next to the title) + replay
            string status = arrived ? StatusArrived : (TutorialStep5Math.Walking(t) ? StatusWalking : StatusReady);
            if (gx.S(7) + mapTitleW + gx.S(18) + statusW + gx.S(8) <= r.Width)
                gx.Text(status, gx.F.MonoSmall, arrived ? pal.Neon : pal.Muted, r.Right - gx.S(8), r.Y + gx.S(5), 2);
            if (arrived)
            {
                float cw = gx.S(56), ch = gx.S(22);
                gx.Chip(ChipReplay, new RectangleF(r.Right - cw - gx.S(8), r.Bottom - ch - gx.S(8), cw, ch), "重播", false);
            }
        }

        private const string MapTitle = "靶场 · 俯视";
        private const string StatusArrived = "状态  已就位", StatusWalking = "状态  走向墙靶", StatusReady = "状态  准备";

        /// <summary>Label index; before arrival never shows 10.0 (the eased walk reaches 10.0 m only on arrival).</summary>
        private static int ShownTenths(double d, bool arrived)
        {
            int i = TutorialStep5Math.TenthsIndex(d, MaxTenths);
            if (!arrived && i <= 100) i = 101;
            if (arrived) i = 100;
            return i;
        }

        private void Measure(TutorialGfx gx)
        {
            if (ReferenceEquals(measuredFont, gx.F.Small) && distLabelW > 0) return;
            measuredFont = gx.F.Small;
            distLabelW = gx.Measure(DistLabel[200], gx.F.Small).Width;
            mapTitleW = gx.Measure(MapTitle, gx.F.MonoSmall).Width;
            statusW = Math.Max(gx.Measure(StatusWalking, gx.F.MonoSmall).Width, gx.Measure(StatusArrived, gx.F.MonoSmall).Width);
        }

        private void DrawFirstPerson(TutorialGfx gx, RectangleF r)
        {
            Graphics g = gx.G;
            gx.RegisterView(r, false);
            TutorialPalette pal = gx.Pal;
            g.SetClip(r);
            g.FillRectangle(gx.B(pal.SceneBg), r);
            double d = TutorialStep5Math.DistanceM(t);
            bool arrived = TutorialStep5Math.Arrived(t);
            float bob = (float)(TutorialStep5Math.Bob(t) * gx.S(2.5f));
            float cx = r.X + r.Width / 2f, horizon = r.Y + r.Height * 0.46f + bob;
            double f = Math.Min(r.Height * 1.5, r.Width * 1.1);
            const double eye = 1.6;

            // floor: lines every metre (fixed in the world, so they scroll toward you while walking) + converging rails
            Pen fl = gx.P(TutorialGfx.A(pal.Neon, 30), 1f);
            for (int m = 1; m <= 40; m++)
            {
                double z = d - Math.Floor(d) + m - 1; // distance from the eye to the m-th line in front (world-aligned)
                if (z < 0.4) continue;
                float y = (float)(horizon + TutorialStep5Math.ProjectPx(eye, z, f));
                if (y > r.Bottom) continue;
                if (z > d) break;
                g.DrawLine(fl, r.X, y, r.Right, y);
            }
            for (int i = -6; i <= 6; i++)
            {
                double xw = i * 1.0;
                float xb = (float)(cx + xw * f / 0.8), yb = (float)(horizon + eye * f / 0.8);
                g.DrawLine(fl, cx + (float)(xw * f / Math.Max(d, 0.5)), (float)(horizon + eye * f / Math.Max(d, 0.5)), xb, yb);
            }
            g.DrawLine(gx.P(TutorialGfx.A(pal.Neon, 55), 1f), r.X, horizon, r.Right, horizon);

            // the wall (2.4 m x 3.2 m 示意), standing on the floor at distance d
            float wh = (float)TutorialStep5Math.ProjectPx(2.4, d, f), ww = (float)TutorialStep5Math.ProjectPx(3.2, d, f);
            float wb = (float)(horizon + TutorialStep5Math.ProjectPx(eye, d, f));
            gx.Wall(cx, wb - wh / 2f, ww, wh, pal.Accent, null);
            // centre dot of the screen
            gx.Dot(Color.FromArgb(230, 255, 255, 255), cx, r.Y + r.Height * 0.46f, gx.S(1.4f));
            g.ResetClip();

            g.DrawRectangle(gx.P(TutorialGfx.A(pal.Neon, 70), 1f), r.X, r.Y, r.Width, r.Height);
            gx.HudFrame(r, TutorialGfx.A(pal.Neon, 200), 10);
            gx.Text("第一人称", gx.F.MonoSmall, TutorialGfx.A(pal.Neon, 210), r.X + gx.S(7), r.Y + gx.S(5), 0);
            Color rc = arrived ? pal.Neon : pal.Cyan;
            gx.Text(DistShort[ShownTenths(d, arrived)], gx.F.Big, rc, r.Right - gx.S(8), r.Y + gx.S(4), 2);
            if (arrived) gx.Tag("10 米 · 墙靶", cx, r.Bottom - gx.S(26), pal.Neon, 1);
        }

        private void DrawChecklist(TutorialGfx gx, RectangleF r)
        {
            Graphics g = gx.G;
            TutorialPalette pal = gx.Pal;
            Panel(gx, r, "准备清单");
            // two blocks: the 3 preparation items, then 「接下来的顺序」 (Leo: 去靶场 -> 先调好上抬 -> 再单独按住 D -> A W S)
            float top = r.Y + gx.S(24), bottom = r.Bottom - gx.S(6);
            float head = gx.S(22);
            int rows = TutorialStep5Math.ItemCount + Order.Length;
            float rowH = Math.Min(gx.S(28), (bottom - top - head) / rows);
            float box = Math.Min(gx.S(15), rowH * 0.72f);
            float bx = r.X + gx.S(10), tx = bx + box + gx.S(8);
            for (int i = 0; i < TutorialStep5Math.ItemCount; i++)
            {
                float y = top + i * rowH + rowH / 2f;
                double k = TutorialStep5Math.Tick01(i, t);
                bool on = k > 0;
                if (on)
                {
                    g.FillRectangle(gx.B(TutorialGfx.A(pal.Neon, (int)(40 + 120 * k))), bx, y - box / 2, box, box);
                    Check(gx, bx, y, box, TutorialGfx.A(Color.Black, (int)(255 * k)));
                }
                g.DrawRectangle(gx.P(on ? pal.Neon : pal.Border, 1.2f), bx, y - box / 2, box, box);
                gx.TextMid(Items[i], gx.F.Small, on ? pal.Text : pal.Muted, tx, y, 0);
            }

            float hy = top + TutorialStep5Math.ItemCount * rowH;
            g.DrawLine(gx.P(TutorialGfx.A(pal.Neon, 50), 1f), bx, hy + gx.S(4), r.Right - gx.S(10), hy + gx.S(4));
            gx.TextMid("接下来的顺序", gx.F.Tag, pal.Accent, bx, hy + head / 2f + gx.S(3), 0);
            bool arrived = TutorialStep5Math.Arrived(t);
            double pulse = 0.5 + 0.5 * Math.Sin(t / 230.0);
            for (int i = 0; i < Order.Length; i++)
            {
                float y = hy + head + i * rowH + rowH / 2f;
                bool now = arrived && (riseSet ? i == 1 : i == 0);
                bool doneRow = riseSet && i == 0;
                Color c = doneRow ? pal.Neon : (now ? pal.Accent : pal.Muted);
                float rr = box / 2f + gx.S(1);
                float ccx = bx + box / 2f;
                if (now) g.FillEllipse(gx.B(TutorialGfx.A(pal.Accent, (int)(40 + 60 * pulse))), ccx - rr - gx.S(3), y - rr - gx.S(3), 2 * rr + gx.S(6), 2 * rr + gx.S(6));
                if (doneRow)
                {
                    g.FillEllipse(gx.B(TutorialGfx.A(pal.Neon, 160)), ccx - rr, y - rr, 2 * rr, 2 * rr);
                    Check(gx, bx, y, box, Color.Black);
                }
                else
                {
                    g.DrawEllipse(gx.P(c, 1.2f), ccx - rr, y - rr, 2 * rr, 2 * rr);
                    gx.TextMid(OrderNum[i], gx.F.Tag, c, ccx, y, 1);
                }
                if (i > 0) g.DrawLine(gx.P(TutorialGfx.A(pal.Border, 200), 1f), ccx, y - rowH + rr, ccx, y - rr);
                gx.TextMid(Order[i], gx.F.Small, now || doneRow ? pal.Text : pal.Muted, tx, y, 0);
            }
        }

        private void Check(TutorialGfx gx, float bx, float y, float box, Color c)
        {
            PointF[] ck = gx.Pts(3);
            ck[0] = new PointF(bx + box * 0.2f, y + box * 0.02f);
            ck[1] = new PointF(bx + box * 0.42f, y + box * 0.26f);
            ck[2] = new PointF(bx + box * 0.82f, y - box * 0.28f);
            gx.G.DrawLines(gx.P(c, 1.8f), ck);
        }
    }
#endif
}
