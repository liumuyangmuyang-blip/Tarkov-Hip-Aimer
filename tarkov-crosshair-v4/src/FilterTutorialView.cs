// FilterTutorialView.cs -- 滤镜教程 (v4.2, W3): the whole tutorial window drawn on one canvas (header + progress dots,
// procedurally drawn scene with the live filter preview, text panel with sliders / chips, footer buttons).
// System.Drawing only (no WinForms): FilterTutorialForm hosts it, the offscreen renderer draws it to PNG.
// Same look as the crosshair tutorial: TutorialPalette.FromTheme(), TutorialFonts, TutorialGfx helpers.
// The preview maps a drawn picture through the filter's real ramp (FilterTutorialModel.Lut). It never reads the screen.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TarkovShade;

namespace TarkovCrosshair
{
    public enum FilterTutHit { None, Prev, Next, Exit, Dot, Slider, Split, Preset, Key }

    public sealed class FilterTutorialView : IDisposable
    {
        public readonly FilterTutorialModel M;
        public readonly TutorialGfx Gx;

        /// <summary>Exit button / Esc.</summary>
        public event Action ExitRequested;
        /// <summary>「完成」 on the last step.</summary>
        public event Action Finished;

        public PointF Mouse = new PointF(-1, -1);
        public bool MouseIsDown;

        // ---- layout (px)
        private Size client;
        private float layoutK = -1;
        private int layoutStep = -1;
        private float layoutFontScale = -1;
        private bool layoutDirty = true;
        private RectangleF rHeader, rFooter, rScene, rPanel, rPrompt, bExit, bPrev, bNext, rControls;
        private RectangleF[] dotRects = new RectangleF[0];
        private RectangleF[] sliderRects = new RectangleF[0];
        private RectangleF[] trackRects = new RectangleF[0];
        private RectangleF[] presetRects = new RectangleF[0];
        private RectangleF[] keyRects = new RectangleF[0];
        private RectangleF rNote, rExtra;
        private bool showSubtitle, showNote, compactNote;
        private readonly TutorialTextBlock tbTitle = new TutorialTextBlock(), tbBody = new TutorialTextBlock(),
            tbNote = new TutorialTextBlock(), tbPrompt = new TutorialTextBlock(), tbExtra = new TutorialTextBlock();
        private float controlsH;

        // ---- interaction
        private int dragSlider = -1;
        private bool dragSplit;
        private int keyChipDown = -1;
        private double timeMs;

        // ---- scene bitmaps
        private Bitmap baseBmp, filtBmp;
        private int[] basePx, filtPx;
        private string filtSig;
        private Size sceneBmpSize;
        private readonly byte[] lr = new byte[256], lg = new byte[256], lb = new byte[256];
        // scene geometry in bitmap fractions (for markers)
        private RectangleF enemyFrac, windowFrac;

        // ---- text records for the layout check (renderer / tests)
        public struct TextRec { public RectangleF R, Box; public string S; public int Group; }
        public readonly List<TextRec> Texts = new List<TextRec>();
        /// <summary>Record texts while drawing (the renderer turns it on).</summary>
        public bool RecordTexts;
        private int group;

        public const string HeaderTitle = "滤镜教程", Subtitle = "TARKOV HIP AIMER";
        public const string LExit = "退出教程", LPrev = "上一步", LNext = "下一步", LFinish = "完成";

        public FilterTutorialView(FilterTutorialModel m, TutorialGfx gx)
        {
            if (m == null) throw new ArgumentNullException("m");
            if (gx == null) throw new ArgumentNullException("gx");
            M = m; Gx = gx;
        }

        private float S(float px) { return Gx.S(px); }

        public void SetClient(Size sz, float k)
        {
            if (sz.Width <= 0 || sz.Height <= 0) return;
            if (sz != client || Math.Abs(k - Gx.K) > 0.0001f) layoutDirty = true;
            client = sz;
            Gx.K = k;
        }

        public void InvalidateLayout() { layoutDirty = true; }

        // ================================================================== layout
        private void Layout(Graphics g)
        {
            layoutDirty = false;
            layoutK = Gx.K;
            layoutStep = M.Step;
            layoutFontScale = Gx.F.Scale;
            float W = client.Width, H = client.Height;
            float pad = S(12);
            FilterTutStep st = M.Current;

            float bigH = Gx.F.Big.GetHeight(g), btnH = Gx.F.Button.GetHeight(g), smallH = Gx.F.Small.GetHeight(g);
            float headerH = Math.Max(S(48), bigH + S(22));
            rHeader = new RectangleF(0, 0, W, headerH);

            // footer: buttons right, prompt left
            float bh = Math.Max(S(38), btnH + S(18));
            float footerH = bh + S(22);
            rFooter = new RectangleF(0, H - footerH, W, footerH);
            float by = rFooter.Top + (footerH - bh) / 2;
            float wExit = BtnW(LExit), wPrev = BtnW(LPrev), wNext = Math.Max(BtnW(LNext), BtnW(LFinish)) + S(16);
            float x = W - pad - wNext;
            bNext = new RectangleF(x, by, wNext, bh);
            x -= S(10) + wPrev; bPrev = new RectangleF(x, by, wPrev, bh);
            x -= S(10) + wExit; bExit = new RectangleF(x, by, wExit, bh);
            rPrompt = new RectangleF(pad, rFooter.Top + S(4), Math.Max(S(60), bExit.Left - S(16) - pad), footerH - S(8));
            float promptW = Math.Max(S(60), rPrompt.Width - S(26));
            tbPrompt.Layout(g, M.Prompt, Gx.F.Body, promptW, 1.1f, 0, Gx.Near);

            // header dots (centre) and subtitle
            int n = FilterTutorialModel.StepCount;
            float dot = S(22);
            float dotsW = n * dot;
            dotRects = new RectangleF[n];
            float dx = (W - dotsW) / 2;
            for (int i = 0; i < n; i++) dotRects[i] = new RectangleF(dx + i * dot, headerH / 2 - dot / 2, dot, dot);
            float titleRight = S(42) + Gx.Measure(HeaderTitle, Gx.F.Big).Width;
            showSubtitle = titleRight + S(10) + Gx.Measure(Subtitle, Gx.F.MonoSmall).Width < dotRects[0].Left - S(16);

            // content: scene left, panel right
            float top = headerH + pad, bottom = rFooter.Top - pad;
            float panelW = Math.Min(Math.Max(W * 0.37f, S(330)), W * 0.47f);
            rPanel = new RectangleF(W - pad - panelW, top, panelW, bottom - top);
            rScene = new RectangleF(pad + S(3), top + S(3), rPanel.Left - pad * 2 - S(6), bottom - top - S(6));

            // panel text
            float ip = S(18), iw = rPanel.Width - 2 * ip;
            tbTitle.Layout(g, st.Title, Gx.F.Title, iw, 1.05f, 0, Gx.Near);
            tbBody.Layout(g, st.Body, Gx.F.Body, iw, 1.2f, 0.25f, Gx.Near);
            tbNote.Layout(g, st.Note ?? "", Gx.F.Note, iw - S(14), 1.15f, 0, Gx.Near);
            string extra = st.Extra != null && st.Extra.Length > 0 ? string.Join("\n", st.Extra) : "";
            tbExtra.Layout(g, extra, Gx.F.Note, iw, 1.15f, 0.1f, Gx.Near);

            // controls block (bottom of the panel)
            float cx = rPanel.Left + ip;
            List<RectangleF> rows = new List<RectangleF>();
            float h = 0;
            sliderRects = new RectangleF[st.Sliders.Length];
            trackRects = new RectangleF[st.Sliders.Length];
            presetRects = new RectangleF[0];
            keyRects = new RectangleF[0];
            float rowH = smallH + S(34);
            float headH = smallH + S(8);
            h += headH;
            for (int i = 0; i < st.Sliders.Length; i++) h += rowH;
            float chipH = btnH + S(14);
            List<string> chips = new List<string>();
            if (st.PresetChips) foreach (PilotStyle p in M.Presets) chips.Add(p.Name);
            if (st.KeyChips) { chips.Add(KeyChip(0)); chips.Add(KeyChip(1)); chips.Add(KeyChip(2)); }
            // chips flow in rows
            List<RectangleF> chipRel = new List<RectangleF>();
            float rx = 0, ry = 0;
            foreach (string c in chips)
            {
                float w = Gx.Measure(c, Gx.F.Button).Width + S(24);
                if (w > iw) w = iw;
                if (rx > 0 && rx + w > iw) { rx = 0; ry += chipH + S(8); }
                chipRel.Add(new RectangleF(rx, ry, w, chipH));
                rx += w + S(8);
            }
            float chipsH = chips.Count > 0 ? ry + chipH : 0;
            h += chipsH;
            if (extra.Length > 0) h += S(10) + tbExtra.Height;
            if (st.Split && !st.PresetChips) h += 0; // the split is dragged in the scene
            controlsH = h;
            float cy = rPanel.Bottom - ip - h;
            rControls = new RectangleF(cx, cy, iw, h);
            float yy = cy + headH;
            for (int i = 0; i < st.Sliders.Length; i++)
            {
                sliderRects[i] = new RectangleF(cx, yy, iw, rowH);
                trackRects[i] = new RectangleF(cx + S(10), yy + smallH + S(16), iw - S(20), S(6));
                yy += rowH;
            }
            RectangleF[] cr = new RectangleF[chipRel.Count];
            for (int i = 0; i < cr.Length; i++) cr[i] = new RectangleF(cx + chipRel[i].X, yy + chipRel[i].Y, chipRel[i].Width, chipRel[i].Height);
            if (st.PresetChips) presetRects = cr;
            if (st.KeyChips) keyRects = cr;
            yy += chipsH;
            rExtra = extra.Length > 0 ? new RectangleF(cx, yy + S(10), iw, tbExtra.Height) : RectangleF.Empty;

            // note card between body and controls, only when it fits whole
            float textBottom = rPanel.Top + ip + tbTitle.Height + S(10) + tbBody.Height;
            float noteH = (st.Note ?? "").Length > 0 ? Gx.F.Small.GetHeight(g) + S(6) + tbNote.Height + S(16) : 0;
            showNote = noteH > 0 && textBottom + S(12) + noteH <= cy - S(10);
            compactNote = false;
            if (!showNote && noteH > 0)
            {
                // short window: the note as one block 「提示：…」 without its title line (never dropped)
                tbNote.Layout(g, st.NoteTitle + "：" + st.Note, Gx.F.Note, iw - S(14), 1.1f, 0, Gx.Near);
                noteH = tbNote.Height + S(10);
                compactNote = true;
                showNote = textBottom + S(8) + noteH <= cy - S(8);
            }
            rNote = showNote ? new RectangleF(cx, textBottom + (compactNote ? S(8) : S(12)), iw, noteH) : RectangleF.Empty;
        }

        private float BtnW(string s) { return Math.Max(S(96), Gx.Measure(s, Gx.F.Button).Width + S(40)); }

        private string LongestPrompt()
        {
            string a = M.Current.Prompt ?? "", b = M.Current.DonePrompt ?? "";
            return a.Length >= b.Length ? a : b;
        }

        /// <summary>Hotkey chip text: 0 toggle, 1 hold, 2 next.</summary>
        private string KeyChip(int i)
        {
            FilterTutorialKeys k = M.Keys;
            if (i == 0) return k.Toggle + " 开关";
            if (i == 1) return "按住 " + k.HoldOriginal + " 看原画面"; // v5.0.2 (UI-C L10): same words as the 滤镜 page
            return k.NextPreset + " 换预设";
        }

        // ================================================================== drawing
        public void Draw(Graphics g, double nowMs)
        {
            timeMs = nowMs;
            try
            {
                Gx.BeginFrame(g);
                Gx.Mouse = Mouse;
                Gx.TimeMs = nowMs;
                if (layoutDirty || layoutStep != M.Step || Math.Abs(layoutK - Gx.K) > 0.0001f ||
                    Math.Abs(layoutFontScale - Gx.F.Scale) > 0.0001f) Layout(g);
                else tbPrompt.Layout(g, M.Prompt, Gx.F.Body, Math.Max(S(60), rPrompt.Width - S(26)), 1.1f, 0, Gx.Near);
                Texts.Clear();
                TutorialPalette p = Gx.Pal;
                g.Clear(p.Bg);
                if (!M.SplitTouched && M.ShowSplit && dragSplit == false)
                    M.AutoSplit((int)Math.Round(50 + 28 * Math.Sin(nowMs / 900.0)));
                Gx.TextBg = p.Panel;
                try { DrawHeader(g); } catch (Exception) { Reset(g); }
                Gx.TextBg = p.SceneBg;
                try { DrawScene(g); } catch (Exception) { Reset(g); }
                Gx.TextBg = p.Panel;
                try { DrawPanel(g); } catch (Exception) { Reset(g); }
                try { DrawFooter(g); } catch (Exception) { Reset(g); }
            }
            catch (Exception) { Reset(g); }
        }

        private static void Reset(Graphics g)
        {
            try { g.ResetClip(); g.ResetTransform(); } catch (Exception) { }
        }

        private void Rec(RectangleF r, RectangleF box, string s, int grp)
        {
            if (!RecordTexts) return;
            TextRec t = new TextRec(); t.R = r; t.Box = box; t.S = s; t.Group = grp;
            Texts.Add(t);
        }

        /// <summary>Text with record. align 0 left / 1 centre / 2 right at x, y = top (mid = vertically centred).</summary>
        private RectangleF Txt(string s, Font f, Color c, float x, float y, int align, bool mid, RectangleF box)
        {
            if (string.IsNullOrEmpty(s)) return RectangleF.Empty;
            SizeF sz = Gx.Measure(s, f);
            float left = align == 1 ? x - sz.Width / 2 : (align == 2 ? x - sz.Width : x);
            float topY = mid ? y - sz.Height / 2 : y;
            if (mid) Gx.TextMid(s, f, c, x, y, align); else Gx.Text(s, f, c, x, y, align);
            RectangleF r = new RectangleF(left, topY, sz.Width, sz.Height);
            Rec(r, box, s, ++group);
            return r;
        }

        private void Block(TutorialTextBlock tb, Font f, Color c, float x, float y, RectangleF box, string what)
        {
            tb.Draw(Gx.G, f, Gx.B(Gx.Readable(c)), x, y, -1, Gx.Near);
            Rec(new RectangleF(x, y, tb.Width, tb.Height), box, what, ++group);
        }

        private void DrawHeader(Graphics g)
        {
            TutorialPalette p = Gx.Pal;
            g.FillRectangle(Gx.B(p.Panel), rHeader);
            g.DrawLine(Gx.P(p.Border, 1f), rHeader.Left, rHeader.Bottom - 0.5f, rHeader.Right, rHeader.Bottom - 0.5f);
            float frac = (M.Step + 1) / (float)FilterTutorialModel.StepCount;
            g.FillRectangle(Gx.B(TutorialGfx.A(p.Neon, 60)), rHeader.Left, rHeader.Bottom - S(3), rHeader.Width * frac, S(3));
            g.FillRectangle(Gx.B(p.Neon), rHeader.Left, rHeader.Bottom - S(1.5f), rHeader.Width * frac, S(1.5f));
            float lx = S(16), ly = rHeader.Top + rHeader.Height / 2;
            Gx.Ring(lx + S(9), ly, S(15), p.Neon, Color.Black, S(2));
            RectangleF tr = Txt(HeaderTitle, Gx.F.Big, p.Text, lx + S(26), ly, 0, true, rHeader);
            if (showSubtitle) Txt(Subtitle, Gx.F.MonoSmall, p.Muted, tr.Right + S(10), ly, 0, true, rHeader);
            for (int i = 0; i < dotRects.Length; i++)
            {
                RectangleF r = dotRects[i];
                float cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
                bool cur = i == M.Step, hover = r.Contains(Mouse);
                Color c = M.IsDone(i) ? p.Neon : (M.Visited(i) ? p.Text : p.Muted);
                if (cur)
                {
                    double pulse = 0.5 + 0.5 * Math.Sin(timeMs / 260.0);
                    Gx.Dot(TutorialGfx.A(p.Neon, (int)(40 + 50 * pulse)), cx, cy, S(8));
                    Gx.Dot(p.Neon, cx, cy, S(4.2f));
                }
                else if (M.IsDone(i) || M.Visited(i)) Gx.Dot(TutorialGfx.A(c, hover ? 255 : 200), cx, cy, S(3.4f));
                else g.DrawEllipse(Gx.P(TutorialGfx.A(c, hover ? 255 : 170), 1.2f), cx - S(3.4f), cy - S(3.4f), S(6.8f), S(6.8f));
            }
            string right = M.Current.Chapter + " · 步骤 " + (M.Step + 1) + " / " + FilterTutorialModel.StepCount;
            Txt(right, Gx.F.Small, p.Text, rHeader.Right - S(18), ly, 2, true, rHeader);
            for (int i = 0; i < dotRects.Length; i++)
            {
                if (!dotRects[i].Contains(Mouse)) continue;
                string t = (i + 1) + " · " + M.Steps[i].Title;
                SizeF sz = Gx.Measure(t, Gx.F.Small);
                float tx = Math.Min(Math.Max(S(4), dotRects[i].Left + dotRects[i].Width / 2 - sz.Width / 2 - S(6)), client.Width - sz.Width - S(16));
                RectangleF tip = new RectangleF(tx, dotRects[i].Bottom + S(6), sz.Width + S(12), sz.Height + S(6));
                g.FillRectangle(Gx.B(p.Surface), tip);
                g.DrawRectangle(Gx.P(p.Border, 1f), tip.X, tip.Y, tip.Width, tip.Height);
                Gx.TextBg = p.Surface;
                Gx.Text(t, Gx.F.Small, p.Text, tip.Left + S(6), tip.Top + S(3), 0);
                Gx.TextBg = p.Panel;
            }
        }

        // ------------------------------------------------------------------ scene
        private void EnsureScene()
        {
            int w = (int)Math.Max(240, Math.Min(760, rScene.Width / 2f));
            int h = (int)Math.Max(135, Math.Min(760, rScene.Height / 2f));
            if (rScene.Width > 0 && rScene.Height > 0) h = (int)Math.Max(100, Math.Round(w * rScene.Height / rScene.Width));
            Size sz = new Size(w, h);
            if (baseBmp != null && sz == sceneBmpSize) return;
            DisposeScene();
            sceneBmpSize = sz;
            baseBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics bg = Graphics.FromImage(baseBmp)) FilterTutorialScene.Draw(bg, w, h, out enemyFrac, out windowFrac);
            basePx = ReadPixels(baseBmp);
            filtBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            filtPx = new int[basePx.Length];
            filtSig = null;
        }

        private static int[] ReadPixels(Bitmap b)
        {
            Rectangle r = new Rectangle(0, 0, b.Width, b.Height);
            BitmapData d = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] px = new int[b.Width * b.Height];
                for (int y = 0; y < b.Height; y++)
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), px, y * b.Width, b.Width);
                return px;
            }
            finally { b.UnlockBits(d); }
        }

        private static void WritePixels(Bitmap b, int[] px)
        {
            Rectangle r = new Rectangle(0, 0, b.Width, b.Height);
            BitmapData d = b.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < b.Height; y++)
                    Marshal.Copy(px, y * b.Width, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), b.Width);
            }
            finally { b.UnlockBits(d); }
        }

        private static string Sig(PilotStyle s)
        {
            if (s == null) return "orig";
            int[] a = s.ToArray();
            return string.Join(",", Array.ConvertAll(a, delegate(int v) { return v.ToString(); })) + "|" + s.Saturation + "|" + s.ApplySaturation + "|" + s.StrengthMode;
        }

        /// <summary>Filtered picture for a style (cached); null style = the original picture.</summary>
        private Bitmap Filtered(PilotStyle s)
        {
            if (s == null) return baseBmp;
            string sig = Sig(s);
            if (sig == filtSig) return filtBmp;
            FilterTutorialModel.Lut(s, lr, lg, lb);
            double sat = FilterTutorialModel.SaturationFactor(s);
            for (int i = 0; i < basePx.Length; i++) filtPx[i] = FilterTutorialModel.Apply(basePx[i], sat, lr, lg, lb);
            WritePixels(filtBmp, filtPx);
            filtSig = sig;
            return filtBmp;
        }

        private RectangleF FracToScreen(RectangleF f)
        {
            return new RectangleF(rScene.X + f.X * rScene.Width, rScene.Y + f.Y * rScene.Height, f.Width * rScene.Width, f.Height * rScene.Height);
        }

        private void DrawScene(Graphics g)
        {
            TutorialPalette p = Gx.Pal;
            EnsureScene();
            PilotStyle fs = M.FilteredStyle();
            Bitmap filt = Filtered(fs);
            InterpolationMode im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            RectangleF src = new RectangleF(0, 0, baseBmp.Width, baseBmp.Height);
            if (M.ShowSplit)
            {
                float sx = rScene.X + rScene.Width * M.Value(FilterTutorialModel.SlSplit) / 100f;
                // left = filtered, right = original (the person stands on the left, so the filtered side shows him)
                g.SetClip(new RectangleF(rScene.X, rScene.Y, sx - rScene.X, rScene.Height));
                g.DrawImage(filt, rScene, src, GraphicsUnit.Pixel);
                g.SetClip(new RectangleF(sx, rScene.Y, rScene.Right - sx, rScene.Height));
                g.DrawImage(baseBmp, rScene, src, GraphicsUnit.Pixel);
                g.ResetClip();
            }
            else g.DrawImage(filt, rScene, src, GraphicsUnit.Pixel);
            g.InterpolationMode = im;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Gx.HudFrame(new RectangleF(rScene.X - S(3), rScene.Y - S(3), rScene.Width + S(6), rScene.Height + S(6)), TutorialGfx.A(p.Neon, 160), 16);

            float m = S(10);
            // bottom-right: what this picture is
            DrawTag("教程画面", rScene.Right - m, rScene.Bottom - m - TagH(), p.Muted, 2);
            int step = M.Step;
            if (M.ShowSplit)
            {
                float sx = rScene.X + rScene.Width * M.Value(FilterTutorialModel.SlSplit) / 100f;
                bool hot = dragSplit || (InScene(Mouse) && Math.Abs(Mouse.X - sx) < S(18));
                g.DrawLine(Gx.P(TutorialGfx.A(Color.Black, 160), 4f), sx, rScene.Top, sx, rScene.Bottom);
                g.DrawLine(Gx.P(hot ? p.Accent : p.Neon, 2f), sx, rScene.Top, sx, rScene.Bottom);
                float cy = rScene.Top + rScene.Height / 2, r = S(13);
                g.FillEllipse(Gx.B(p.Panel), sx - r, cy - r, 2 * r, 2 * r);
                g.DrawEllipse(Gx.P(hot ? p.Accent : p.Neon, 2f), sx - r, cy - r, 2 * r, 2 * r);
                PointF[] a = Gx.Pts(3);
                a[0] = new PointF(sx - S(3), cy - S(5)); a[1] = new PointF(sx - S(8), cy); a[2] = new PointF(sx - S(3), cy + S(5));
                g.FillPolygon(Gx.B(hot ? p.Accent : p.Neon), a);
                a[0] = new PointF(sx + S(3), cy - S(5)); a[1] = new PointF(sx + S(8), cy); a[2] = new PointF(sx + S(3), cy + S(5));
                g.FillPolygon(Gx.B(hot ? p.Accent : p.Neon), a);
                string name = step == FilterTutorialModel.StepIntro ? "滤镜：通用" : "滤镜：" + M.Presets[M.PresetIndex].Name;
                DrawTag(name, rScene.Left + m, rScene.Top + m, p.Neon, 0);
                DrawTag("原画", rScene.Right - m, rScene.Top + m, p.Text, 2);
            }
            else if (step == FilterTutorialModel.StepShadows || step == FilterTutorialModel.StepHighlights || step == FilterTutorialModel.StepSaturation)
            {
                DrawTag("滤镜：教程滑块", rScene.Left + m, rScene.Top + m, p.Neon, 0);
            }
            if (step == FilterTutorialModel.StepShadows)
            {
                bool seen = FilterTutorialModel.EnemySeen(M.SliderStyle());
                RectangleF e = FracToScreen(enemyFrac);
                e.Inflate(S(6), S(6));
                Color c = seen ? p.Neon : p.Accent;
                if (seen) g.DrawRectangle(Gx.Dashed(c, 1.6f), e.X, e.Y, e.Width, e.Height);
                DrawTag(seen ? "看见了：敌人" : "这里有人？", e.Left, Math.Max(rScene.Top + m + TagH() + S(8), e.Top - TagH() - S(6)), c, 0);
            }
            else if (step == FilterTutorialModel.StepHighlights)
            {
                bool calm = FilterTutorialModel.SkyCalm(M.SliderStyle());
                RectangleF w = FracToScreen(windowFrac);
                DrawTag(calm ? "窗外：不刺眼" : "窗外：太亮", w.Left + w.Width / 2, w.Bottom + S(8), calm ? p.Neon : p.Accent, 1);
            }
            else if (step == FilterTutorialModel.StepSaturation)
            {
                int v = M.Value(FilterTutorialModel.SlSaturation);
                string t = v == PilotStyle.SaturationNeutral ? "颜色：不变" : (v > PilotStyle.SaturationNeutral ? "颜色：更浓" : "颜色：更淡");
                DrawTag(t, rScene.Left + m, rScene.Top + m + TagH() + S(8), p.Accent, 0);
            }
            else if (step == FilterTutorialModel.StepTune)
                DrawTag(M.TuneTag(), rScene.Left + m, rScene.Top + m + TagH() + S(8), p.Accent, 0); // v5.1
            else if (step == FilterTutorialModel.StepKeys)
            {
                Color c = M.HoldOriginal ? p.Accent : (M.FilterOn ? p.Neon : p.Muted);
                DrawTag(M.KeyStatus(), rScene.Left + m, rScene.Top + m, c, 0);
            }
        }

        private float TagH() { return Gx.F.Tag.GetHeight(Gx.G) + S(5); }

        private void DrawTag(string s, float x, float y, Color c, int align)
        {
            RectangleF r = Gx.Tag(s, x, y, c, align);
            Rec(r, rScene, s, ++group);
        }

        private bool InScene(PointF pt) { return rScene.Contains(pt); }

        // ------------------------------------------------------------------ panel
        private void DrawPanel(Graphics g)
        {
            TutorialPalette p = Gx.Pal;
            FilterTutStep st = M.Current;
            RectangleF r = rPanel;
            g.FillRectangle(Gx.B(p.Panel), r);
            g.DrawRectangle(Gx.P(p.Border, 1f), r.X, r.Y, r.Width, r.Height);
            g.FillRectangle(Gx.B(p.Neon), r.Left, r.Top, S(3), S(40));
            float ip = S(18), x = r.Left + ip, y = r.Top + ip;
            Block(tbTitle, Gx.F.Title, p.Text, x, y, r, "title");
            y += tbTitle.Height + S(10);
            Block(tbBody, Gx.F.Body, p.Text, x, y, r, "body");
            if (showNote)
            {
                RectangleF n = rNote;
                bool safety = st.KeyChips;
                Color edge = safety ? p.Neon : p.Accent;
                g.FillRectangle(Gx.B(TutorialGfx.Mix(p.Panel, edge, 0.08)), n);
                g.FillRectangle(Gx.B(edge), n.Left, n.Top, S(3), n.Height);
                Gx.TextBg = TutorialGfx.Mix(p.Panel, edge, 0.08);
                if (compactNote) Block(tbNote, Gx.F.Note, p.Text, n.Left + S(12), n.Top + S(5), n, "note");
                else
                {
                    RectangleF tr = Txt(st.NoteTitle, Gx.F.Small, edge, n.Left + S(12), n.Top + S(8), 0, false, n);
                    Block(tbNote, Gx.F.Note, p.Text, n.Left + S(12), tr.Bottom + S(4), n, "note");
                }
                Gx.TextBg = p.Panel;
            }
            // controls
            RectangleF c = rControls;
            g.DrawLine(Gx.P(p.Border, 1f), c.Left, c.Top - S(4), c.Right, c.Top - S(4));
            string head = st.KeyChips ? "试一试（教程里不会改屏幕）" : (st.PresetChips ? "选预设" : (st.Sliders.Length > 0 ? "拖动滑块" : "在画面里拖动对比线"));
            Txt(head, Gx.F.Small, p.Muted, c.Left, c.Top + S(2), 0, false, r);
            for (int i = 0; i < sliderRects.Length; i++) DrawSlider(g, i, st.Sliders[i]);
            for (int i = 0; i < presetRects.Length; i++)
            {
                RectangleF ch = Gx.Chip(100 + i, presetRects[i], M.Presets[i].Name, i == M.PresetIndex);
                Rec(ch, r, M.Presets[i].Name, ++group);
            }
            for (int i = 0; i < keyRects.Length; i++)
            {
                bool active = i == 0 ? M.KeyToggled : (i == 1 ? M.HoldOriginal || (M.KeyHeld && keyChipDown != 1) : M.KeyNext);
                RectangleF ch = Gx.Chip(200 + i, keyRects[i], KeyChip(i), i == 1 ? M.HoldOriginal : active);
                if (i == 1 && M.KeyHeld && !M.HoldOriginal) g.DrawRectangle(Gx.P(TutorialGfx.A(p.Neon, 140), 1.2f), ch.X + S(2), ch.Y + S(2), ch.Width - S(4), ch.Height - S(4));
                Rec(ch, r, KeyChip(i), ++group);
            }
            if (!rExtra.IsEmpty) Block(tbExtra, Gx.F.Note, p.Muted, rExtra.Left, rExtra.Top, r, "extra");
        }

        private void DrawSlider(Graphics g, int row, int id)
        {
            TutorialPalette p = Gx.Pal;
            FilterTutSlider d = M.SliderDefs[id];
            RectangleF rr = sliderRects[row], t = trackRects[row];
            bool hot = dragSlider == id || rr.Contains(Mouse);
            int v = M.Value(id);
            Txt(d.Label, Gx.F.Small, hot ? p.Text : p.Text, rr.Left, rr.Top + S(4), 0, false, rPanel);
            string vs = v.ToString();
            if (id == FilterTutorialModel.SlSaturation && v == d.Neutral) vs = v + "（不变）";
            if (id == FilterTutorialModel.SlTune) vs = v > 0 ? "+" + v : (v == 0 ? "0（预设）" : v.ToString()); // v5.1: 11 steps
            Txt(vs, Gx.F.MonoSmall, p.Neon, rr.Right, rr.Top + S(4), 2, false, rPanel);
            float f = (v - d.Min) / (float)Math.Max(1, d.Max - d.Min);
            float kx = t.Left + t.Width * f, ky = t.Top + t.Height / 2;
            g.FillRectangle(Gx.B(p.Surface), t);
            g.DrawRectangle(Gx.P(p.Border, 1f), t.X, t.Y, t.Width, t.Height);
            float nf = (d.Neutral - d.Min) / (float)Math.Max(1, d.Max - d.Min);
            float nx = t.Left + t.Width * nf;
            float a = Math.Min(nx, kx), b = Math.Max(nx, kx);
            g.FillRectangle(Gx.B(p.Neon), a, t.Top, b - a, t.Height);
            g.DrawLine(Gx.P(p.Muted, 1.2f), nx, t.Top - S(4), nx, t.Bottom + S(4));
            float kr = S(9);
            g.FillEllipse(Gx.B(hot ? TutorialGfx.Mix(p.Neon, Color.White, 0.2) : p.Neon), kx - kr, ky - kr, 2 * kr, 2 * kr);
            g.DrawEllipse(Gx.P(Color.Black, 1.5f), kx - kr, ky - kr, 2 * kr, 2 * kr);
        }

        // ------------------------------------------------------------------ footer
        private void DrawFooter(Graphics g)
        {
            TutorialPalette p = Gx.Pal;
            g.FillRectangle(Gx.B(p.Panel), rFooter);
            g.DrawLine(Gx.P(p.Border, 1f), rFooter.Left, rFooter.Top + 0.5f, rFooter.Right, rFooter.Top + 0.5f);
            double pulse = 0.5 + 0.5 * Math.Sin(timeMs / 300.0);
            bool done = M.IsDone(M.Step);
            float px = rPrompt.Left + S(4), py = rPrompt.Top + rPrompt.Height / 2;
            PointF[] tri = Gx.Pts(3);
            tri[0] = new PointF(px, py - S(6)); tri[1] = new PointF(px + S(9), py); tri[2] = new PointF(px, py + S(6));
            g.FillPolygon(Gx.B(TutorialGfx.A(p.Neon, (int)(150 + 105 * pulse))), tri);
            Block(tbPrompt, Gx.F.Body, done ? p.Neon : p.Text, px + S(20), py - tbPrompt.Height / 2, rPrompt, "prompt");
            Button(g, bExit, LExit, false, false);
            Button(g, bPrev, LPrev, false, M.Step == 0);
            Button(g, bNext, M.IsLast ? LFinish : LNext, true, false);
            if (done && !M.IsLast)
                g.DrawRectangle(Gx.P(TutorialGfx.A(p.Neon, (int)(60 + 90 * pulse)), 5f), bNext.X, bNext.Y, bNext.Width, bNext.Height);
        }

        private void Button(Graphics g, RectangleF r, string text, bool primary, bool disabled)
        {
            TutorialPalette p = Gx.Pal;
            bool hover = !disabled && r.Contains(Mouse);
            bool press = hover && MouseIsDown;
            Color fill = primary ? (press ? TutorialGfx.Mix(p.Neon, Color.Black, 0.35) : (hover ? TutorialGfx.Mix(p.Neon, Color.White, 0.15) : p.Neon))
                : (press ? p.Border : (hover ? Color.FromArgb(0x38, 0x3D, 0x43) : p.Surface));
            if (disabled) fill = p.Surface;
            g.FillRectangle(Gx.B(fill), r);
            g.DrawRectangle(Gx.P(disabled ? TutorialGfx.A(p.Border, 120) : (primary ? p.Neon : (hover ? p.Accent : p.Border)), 1.2f), r.X, r.Y, r.Width, r.Height);
            Color tc = disabled ? TutorialGfx.Mix(p.Muted, p.Surface, 0.45) : (primary ? Color.Black : p.Text);
            g.DrawString(text, Gx.F.Button, Gx.B(tc), r, Gx.CenterMid);
            SizeF sz = Gx.Measure(text, Gx.F.Button);
            Rec(new RectangleF(r.X + (r.Width - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2, sz.Width, sz.Height), r, text, ++group);
        }

        // ================================================================== input
        public FilterTutHit HitTest(PointF pt, out int index)
        {
            index = -1;
            if (bNext.Contains(pt)) return FilterTutHit.Next;
            if (bPrev.Contains(pt)) return M.Step > 0 ? FilterTutHit.Prev : FilterTutHit.None;
            if (bExit.Contains(pt)) return FilterTutHit.Exit;
            for (int i = 0; i < dotRects.Length; i++) if (dotRects[i].Contains(pt)) { index = i; return FilterTutHit.Dot; }
            for (int i = 0; i < sliderRects.Length; i++) if (sliderRects[i].Contains(pt)) { index = M.Current.Sliders[i]; return FilterTutHit.Slider; }
            for (int i = 0; i < presetRects.Length; i++) if (presetRects[i].Contains(pt)) { index = i; return FilterTutHit.Preset; }
            for (int i = 0; i < keyRects.Length; i++) if (keyRects[i].Contains(pt)) { index = i; return FilterTutHit.Key; }
            if (M.ShowSplit && rScene.Contains(pt)) return FilterTutHit.Split;
            return FilterTutHit.None;
        }

        /// <summary>Mouse button down. Returns true when something changed (repaint).</summary>
        public bool OnMouseDown(PointF pt)
        {
            Mouse = pt; MouseIsDown = true;
            int i;
            FilterTutHit h = HitTest(pt, out i);
            switch (h)
            {
                case FilterTutHit.Slider: dragSlider = i; DragSliderTo(pt.X); return true;
                case FilterTutHit.Split: dragSplit = true; DragSplitTo(pt.X); return true;
                case FilterTutHit.Preset: M.SelectPreset(i); return true;
                case FilterTutHit.Key:
                    keyChipDown = i;
                    if (i == 0) M.PressToggle(); else if (i == 1) M.SetHold(true); else M.PressNext();
                    return true;
            }
            return true;
        }

        public bool OnMouseMove(PointF pt)
        {
            Mouse = pt;
            if (dragSlider >= 0) { DragSliderTo(pt.X); return true; }
            if (dragSplit) { DragSplitTo(pt.X); return true; }
            return true;
        }

        public bool OnMouseUp(PointF pt)
        {
            Mouse = pt;
            bool wasDown = MouseIsDown;
            MouseIsDown = false;
            bool drag = dragSlider >= 0 || dragSplit;
            dragSlider = -1; dragSplit = false;
            if (keyChipDown == 1) M.SetHold(false);
            keyChipDown = -1;
            if (drag || !wasDown) return true;
            int i;
            FilterTutHit h = HitTest(pt, out i);
            switch (h)
            {
                case FilterTutHit.Next:
                    if (M.IsLast) { Action f = Finished; if (f != null) f(); }
                    else M.Next();
                    break;
                case FilterTutHit.Prev: M.Prev(); break;
                case FilterTutHit.Exit: { Action e = ExitRequested; if (e != null) e(); } break;
                case FilterTutHit.Dot: M.Go(i); break;
            }
            return true;
        }

        public void OnMouseLeave() { Mouse = new PointF(-1, -1); }

        private void DragSliderTo(float x)
        {
            int row = Array.IndexOf(M.Current.Sliders, dragSlider);
            if (row < 0 || row >= trackRects.Length) return;
            RectangleF t = trackRects[row];
            FilterTutSlider d = M.SliderDefs[dragSlider];
            float f = (x - t.Left) / Math.Max(1f, t.Width);
            if (f < 0) f = 0; if (f > 1) f = 1;
            M.SetValue(dragSlider, (int)Math.Round(d.Min + f * (d.Max - d.Min)));
        }

        private void DragSplitTo(float x)
        {
            float f = (x - rScene.Left) / Math.Max(1f, rScene.Width);
            if (f < 0) f = 0; if (f > 1) f = 1;
            M.SetValue(FilterTutorialModel.SlSplit, (int)Math.Round(f * 100));
        }

        /// <summary>Keyboard: arrows nudge the first slider of the step (or the split), Enter = next, Esc = exit.
        /// Hotkey demo keys go to the model. Returns true when handled.</summary>
        public bool OnKeyDown(int vk, bool ctrl, bool shift)
        {
            if (M.KeyDown(vk, ctrl)) return true;
            const int VkLeft = 0x25, VkRight = 0x27, VkReturn = 0x0D, VkEscape = 0x1B, VkPrior = 0x21, VkNext = 0x22;
            if (vk == VkEscape) { Action e = ExitRequested; if (e != null) e(); return true; }
            if (vk == VkReturn || vk == VkNext)
            {
                if (M.IsLast) { Action f = Finished; if (f != null) f(); } else M.Next();
                return true;
            }
            if (vk == VkPrior) { M.Prev(); return true; }
            if (vk == VkLeft || vk == VkRight)
            {
                int id = M.Current.Sliders.Length > 0 ? M.Current.Sliders[0] : (M.ShowSplit ? FilterTutorialModel.SlSplit : -1);
                if (id < 0) return false;
                int stepV = id == FilterTutorialModel.SlTune ? 1 : (shift ? 10 : 2); // v5.1: the tune bar moves one step
                M.SetValue(id, M.Value(id) + (vk == VkRight ? stepV : -stepV));
                return true;
            }
            return false;
        }

        public bool OnKeyUp(int vk) { return M.KeyUp(vk); }

        /// <summary>Hover tooltip text for the slider under the mouse ("" = none): the filter's own help text.</summary>
        public string TooltipAt(PointF pt)
        {
            for (int i = 0; i < sliderRects.Length; i++)
                if (sliderRects[i].Contains(pt))
                {
                    string key = M.SliderDefs[M.Current.Sliders[i]].HelpKey;
                    // v5.0.2 (UI-C M1): the app never uses DDC/CI saturation (same text as the 滤镜 page)
                    return key == PilotHelpText.Saturation
                        ? "整体颜色浓淡：50 不变，往上更浓，往下更淡。用显卡驱动还是 Windows 放大镜，在「饱和度方式」里选。"
                        : PilotHelpText.Tooltip(key).Replace("Gamma 曲线", "颜色曲线");
                }
            return "";
        }

        // ================================================================== layout check (renderer / tests)
        /// <summary>Problems in the last drawn frame (needs RecordTexts): text outside its box, or two texts overlapping.</summary>
        public List<string> CheckLayout()
        {
            List<string> bad = new List<string>();
            float tol = 0.75f;
            for (int i = 0; i < Texts.Count; i++)
            {
                TextRec t = Texts[i];
                if (t.R.Left < t.Box.Left - tol || t.R.Top < t.Box.Top - tol || t.R.Right > t.Box.Right + tol || t.R.Bottom > t.Box.Bottom + tol)
                    bad.Add("clipped: '" + t.S + "' " + Fmt(t.R) + " outside " + Fmt(t.Box));
                if (t.R.Left < -tol || t.R.Top < -tol || t.R.Right > client.Width + tol || t.R.Bottom > client.Height + tol)
                    bad.Add("off-window: '" + t.S + "' " + Fmt(t.R));
                for (int j = i + 1; j < Texts.Count; j++)
                {
                    RectangleF a = t.R, b = Texts[j].R;
                    a.Inflate(-tol, -tol); b.Inflate(-tol, -tol);
                    if (a.IntersectsWith(b)) bad.Add("overlap: '" + t.S + "' " + Fmt(t.R) + " x '" + Texts[j].S + "' " + Fmt(Texts[j].R));
                }
            }
            return bad;
        }

        private static string Fmt(RectangleF r)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "[{0:0.#},{1:0.#} {2:0.#}x{3:0.#}]", r.X, r.Y, r.Width, r.Height);
        }

        /// <summary>For the renderer: true when the note card of this step was shown.</summary>
        public bool NoteShown { get { return showNote; } }
        public RectangleF SceneRect { get { return rScene; } }
        public RectangleF TrackRect(int row) { return row >= 0 && row < trackRects.Length ? trackRects[row] : RectangleF.Empty; }
        public RectangleF KeyChipRect(int i) { return i >= 0 && i < keyRects.Length ? keyRects[i] : RectangleF.Empty; }
        public RectangleF PresetChipRect(int i) { return i >= 0 && i < presetRects.Length ? presetRects[i] : RectangleF.Empty; }

        private void DisposeScene()
        {
            if (baseBmp != null) { baseBmp.Dispose(); baseBmp = null; }
            if (filtBmp != null) { filtBmp.Dispose(); filtBmp = null; }
            basePx = null; filtPx = null; filtSig = null;
        }

        public void Dispose() { DisposeScene(); }
    }
}
