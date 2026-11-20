// PURE file: only System + System.Runtime.Serialization. Compiles under net48 (C# 5) and net8.0.
// v4.0 准星样式: the new crosshair shapes (every one keeps the centre empty), the style tiles of the 外观 page and the
// per-profile style data (CrosshairStyle).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace TarkovCrosshair
{
    /// <summary>
    /// v4.0 per-profile style data (saved as Profile.Style). Kind "" = the v3 shapes (Profile.Shape) exactly as before;
    /// any other Kind = one of StyleShapes.Kinds. Size / thickness / colours / outline / opacity / glow / rotation stay
    /// in the Profile fields (Diameter, Thickness, Color, Color2, Gradient, OuterThickness, OpacityPct, GlowPx, ...).
    /// </summary>
    [DataContract]
    public sealed class CrosshairStyle
    {
        /// <summary>The centre is always empty: at least this radius (px) around the centre has alpha 0.</summary>
        public const double MinHole = 3;
        public const double MaxHole = 40;

        [DataMember(Name = "Kind")] public string Kind;
        /// <summary>Distance from the centre to where the shape starts (箭头下沉 / 十字间隙), px.</summary>
        [DataMember(Name = "Gap")] public double Gap;
        /// <summary>Arm / leg / tick length, px.</summary>
        [DataMember(Name = "Length")] public double Length;
        /// <summary>Angle in degrees: 箭头张开角度 (full angle between the legs), 每段弧的角度, 下半弧的弧度.</summary>
        [DataMember(Name = "Spread")] public double Spread;
        /// <summary>Number of dots / arcs.</summary>
        [DataMember(Name = "Count")] public int Count;
        /// <summary>中心留空 radius, px (never below MinHole).</summary>
        [DataMember(Name = "Hole")] public double Hole;
        /// <summary>自定义图片: file name only, inside 「准星样式 Crosshairs」.</summary>
        [DataMember(Name = "PngFile")] public string PngFile;
        /// <summary>自定义图片: true = recolour the picture with 颜色 (keeps its shape / transparency).</summary>
        [DataMember(Name = "PngTint")] public bool PngTint;

        public CrosshairStyle() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        public void SetDefaults()
        {
            Kind = ""; Gap = 4; Length = 7; Spread = 120; Count = 3; Hole = MinHole; PngFile = ""; PngTint = false;
        }

        public CrosshairStyle Clone() { return (CrosshairStyle)MemberwiseClone(); }

        private static double C(double v, double min, double max, double def)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return def;
            return v < min ? min : (v > max ? max : v);
        }

        public void Validate()
        {
            if (Kind == null || Array.IndexOf(StyleShapes.Kinds, Kind) < 0) Kind = "";
            Gap = C(Gap, 0, 100, 4);
            Length = C(Length, 0.5, 100, 7);
            Spread = C(Spread, 5, 340, 120);
            if (Count < 2) Count = 2;
            if (Count > 24) Count = 24;
            Hole = C(Hole, MinHole, MaxHole, MinHole);
            PngFile = StyleShapes.SafeFileName(PngFile);
        }

        public string Key()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
                Kind, Gap, Length, Spread, Count, Hole, PngFile, PngTint);
        }
    }

    /// <summary>A decoded picture for 自定义图片: premultiplied 0xAARRGGBB, top-down, W × H.</summary>
    public sealed class PngImage
    {
        public readonly int[] Pix;
        public readonly int W, H;
        /// <summary>Changes whenever the pixels change (part of the raster key).</summary>
        public readonly string Id;
        /// <summary>v4.1: farthest visible pixel (alpha ≥ 32) from the picture centre, as a share of the longer side
        /// (0 = nothing visible). Drawn at Diameter D the picture reaches Reach × D px from the crosshair centre.</summary>
        public readonly double Reach;
        /// <summary>v4.1: drawn while a picture is still being read on another thread (fully transparent, no warning).</summary>
        public static readonly PngImage Loading = new PngImage(new int[1], 1, 1, "loading");

        private PngImage[] levels; // [0] = this, [k] = half size of [k-1]; made on first use

        public PngImage(int[] pix, int w, int h, string id)
        {
            Pix = pix; W = w; H = h; Id = id ?? "";
            Reach = ReachOf(pix, w, h);
        }

        private static double ReachOf(int[] pix, int w, int h)
        {
            if (pix == null || w < 1 || h < 1 || (long)w * h > pix.Length) return 0;
            double best = 0, cx = w / 2.0, cy = h / 2.0;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                double dy = Math.Abs(y + 0.5 - cy) + 0.5;
                for (int x = 0; x < w; x++)
                {
                    if (((uint)pix[row + x] >> 24) < 32) continue;
                    double dx = Math.Abs(x + 0.5 - cx) + 0.5; // the pixel's far corner
                    double d = dx * dx + dy * dy;
                    if (d > best) best = d;
                }
            }
            return Math.Sqrt(best) / Math.Max(w, h);
        }

        /// <summary>v4.1: the picture at 1 / 2^k size (premultiplied box filter, odd edges averaged over the pixels that
        /// exist); k past the last level (1 × 1) gives the last level. Thread-safe (a race only builds a level twice).</summary>
        public PngImage Level(int k)
        {
            if (k <= 0 || Pix == null || W < 1 || H < 1 || (long)W * H > Pix.Length) return this;
            PngImage[] lv = levels;
            if (lv == null)
            {
                int n = 1;
                for (int w = W, h = H; w > 1 || h > 1; w = (w + 1) / 2, h = (h + 1) / 2) n++;
                lv = new PngImage[n];
                lv[0] = this;
                levels = lv;
            }
            if (k >= lv.Length) k = lv.Length - 1;
            for (int i = 1; i <= k; i++)
                if (lv[i] == null) lv[i] = Half(lv[i - 1], i);
            return lv[k];
        }

        private PngImage Half(PngImage a, int k)
        {
            int w = (a.W + 1) / 2, h = (a.H + 1) / 2;
            int[] o = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sa = 0, sr = 0, sg = 0, sb = 0, n = 0;
                    for (int yy = 2 * y; yy < Math.Min(a.H, 2 * y + 2); yy++)
                        for (int xx = 2 * x; xx < Math.Min(a.W, 2 * x + 2); xx++)
                        {
                            int c = a.Pix[yy * a.W + xx];
                            sa += (int)((uint)c >> 24); sr += (c >> 16) & 0xFF; sg += (c >> 8) & 0xFF; sb += c & 0xFF; n++;
                        }
                    int h2 = n / 2;
                    int A = (sa + h2) / n, R = (sr + h2) / n, G = (sg + h2) / n, B = (sb + h2) / n;
                    if (R > A) R = A; if (G > A) G = A; if (B > A) B = A;
                    o[y * w + x] = (A << 24) | (R << 16) | (G << 8) | B;
                }
            return new PngImage(o, w, h, Id + "|L" + k);
        }
    }

    /// <summary>
    /// v4.1 (BRUTE40 P2-8/P2-9) picture cache policy for 自定义图片 (pure; CustomCrosshair supplies the file system and the
    /// decoder). Get(name, mayBlock): a cached picture; on a thread that may not block (the overlay) a picture not read
    /// yet is read in the Background and PngImage.Loading is returned meanwhile; a picture that failed is tried again
    /// once its file changes (Stamp, checked at most every RetryMs).
    /// </summary>
    public sealed class PngCache
    {
        public const long RetryMs = 1000;
        /// <summary>File name -> change stamp (e.g. last write time ^ length); long.MinValue = no such file.</summary>
        public Func<string, long> Stamp;
        /// <summary>File name -> decoded picture, or null when it is not a usable picture (may take long).</summary>
        public Func<string, long, PngImage> Decode;
        /// <summary>Milliseconds clock.</summary>
        public Func<long> NowMs;
        /// <summary>Runs work on another thread (the app: the thread pool).</summary>
        public Action<Action> Background;

        /// <summary>v4.1 memory bound (Leo 17 Nov 2026): at most this many pictures and this many pixels (level 0; the
        /// half-size levels add at most a third) stay cached; the least recently used go first, rebuilt on demand.</summary>
        public int MaxPictures = 4;
        public long MaxPixels = 3L * 512 * 512;
        /// <summary>A picture the overlay (a non-blocking caller) used within this time is never evicted, and survives TrimUi.</summary>
        public const long KeepMs = 2000;

        private sealed class Fail { public long Stamp, CheckedAt; }
        private sealed class Item { public PngImage Img; public long Use, OverlayUse = long.MinValue; }

        private readonly object gate = new object();
        private readonly Dictionary<string, Item> ok = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Fail> bad = new Dictionary<string, Fail>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private long pixels;

        /// <summary>Pictures / level-0 pixels cached now.</summary>
        public int Count { get { lock (gate) return ok.Count; } }
        public long Pixels { get { lock (gate) return pixels; } }

        public PngImage Get(string name, bool mayBlock)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Fail f;
            lock (gate)
            {
                Item it;
                if (ok.TryGetValue(name, out it))
                {
                    long t = Now();
                    it.Use = t;
                    if (!mayBlock) it.OverlayUse = t;
                    return it.Img;
                }
                if (busy.Contains(name) && !mayBlock) return bad.ContainsKey(name) ? null : PngImage.Loading; // a blocking caller reads it itself
                if (bad.TryGetValue(name, out f))
                {
                    long now = Now();
                    if (now - f.CheckedAt < RetryMs && now >= f.CheckedAt) return null;
                    f.CheckedAt = now;
                }
            }
            // v5.0.2 (W16, AUDIT F11): a caller that may not block (the overlay's render thread) never touches the file
            // system: the change stamp is read on the Background thread too (a cold disk, a network or cloud folder, an
            // antivirus scan could take tens of ms). Meanwhile a picture that failed stays null and a new one is Loading.
            if (!mayBlock && Background != null)
            {
                Fail known = f;
                lock (gate)
                {
                    Item it;
                    if (ok.TryGetValue(name, out it)) return it.Img;
                    if (!busy.Add(name)) return known != null ? null : PngImage.Loading;
                }
                try
                {
                    Background(delegate
                    {
                        try
                        {
                            long st = SafeStamp(name);
                            if (known != null)
                            {
                                if (st == known.Stamp) return; // unchanged: still broken / missing
                                lock (gate) bad.Remove(name);
                            }
                            Load(name, st, true);
                        }
                        finally { lock (gate) busy.Remove(name); }
                    });
                }
                catch (Exception) { lock (gate) busy.Remove(name); return null; }
                return known != null ? null : PngImage.Loading;
            }
            long stamp = SafeStamp(name);
            if (f != null)
            {
                if (stamp == f.Stamp) return null; // unchanged: still broken / missing
                lock (gate) bad.Remove(name);
            }
            if (mayBlock || Background == null) return Load(name, stamp, !mayBlock);
            lock (gate)
            {
                Item it;
                if (ok.TryGetValue(name, out it)) return it.Img;
                if (!busy.Add(name)) return PngImage.Loading;
            }
            try { Background(delegate { try { Load(name, stamp, true); } finally { lock (gate) busy.Remove(name); } }); }
            catch (Exception) { lock (gate) busy.Remove(name); return null; }
            return PngImage.Loading;
        }

        /// <summary>v5.0.2 (W16, BRUTE P3-5): stores a picture already decoded (the upload's check decode), so it is not
        /// decoded a second time.</summary>
        public void Put(string name, PngImage img)
        {
            if (string.IsNullOrEmpty(name) || img == null) return;
            lock (gate)
            {
                Item old;
                if (ok.TryGetValue(name, out old)) { pixels -= PixelsOf(old.Img); ok.Remove(name); }
                long now = Now();
                Item it = new Item(); it.Img = img; it.Use = now;
                ok[name] = it; pixels += PixelsOf(img);
                bad.Remove(name);
                Evict(name, now);
            }
        }

        private static long PixelsOf(PngImage img) { return img == null ? 0 : (long)img.W * img.H; }

        /// <summary>Drops least recently used pictures past MaxPictures / MaxPixels (never 'keep', never one the overlay used
        /// within KeepMs). Under gate.</summary>
        private void Evict(string keep, long now)
        {
            while (ok.Count > Math.Max(1, MaxPictures) || pixels > MaxPixels)
            {
                string victim = null;
                long oldest = long.MaxValue;
                foreach (KeyValuePair<string, Item> kv in ok)
                {
                    if (string.Equals(kv.Key, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    if (kv.Value.OverlayUse != long.MinValue && now - kv.Value.OverlayUse < KeepMs) continue;
                    if (kv.Value.Use < oldest) { oldest = kv.Value.Use; victim = kv.Key; }
                }
                if (victim == null) return;
                pixels -= PixelsOf(ok[victim].Img);
                ok.Remove(victim);
            }
        }

        /// <summary>
        /// v4.1 (Leo 17 Nov 2026: in game only the overlay stays): forget every picture the overlay has not used within
        /// KeepMs (what only the settings window's previews needed) and the failed-file notes. Returns how many were dropped.
        /// </summary>
        public int TrimUi()
        {
            lock (gate)
            {
                long now = Now();
                List<string> drop = new List<string>();
                foreach (KeyValuePair<string, Item> kv in ok)
                    if (kv.Value.OverlayUse == long.MinValue || now - kv.Value.OverlayUse >= KeepMs) drop.Add(kv.Key);
                foreach (string n in drop) { pixels -= PixelsOf(ok[n].Img); ok.Remove(n); }
                bad.Clear();
                return drop.Count;
            }
        }

        private long Now() { Func<long> n = NowMs; try { return n == null ? 0 : n(); } catch (Exception) { return 0; } }

        private long SafeStamp(string name)
        {
            Func<string, long> s = Stamp;
            try { return s == null ? long.MinValue : s(name); } catch (Exception) { return long.MinValue; }
        }

        private PngImage Load(string name, long stamp, bool forOverlay)
        {
            PngImage img = null;
            Func<string, long, PngImage> d = Decode;
            if (stamp != long.MinValue && d != null) { try { img = d(name, stamp); } catch (Exception) { img = null; } }
            lock (gate)
            {
                Item old;
                if (ok.TryGetValue(name, out old)) { pixels -= PixelsOf(old.Img); ok.Remove(name); }
                if (img != null)
                {
                    long now = Now();
                    Item it = new Item(); it.Img = img; it.Use = now; if (forOverlay) it.OverlayUse = now;
                    ok[name] = it; pixels += PixelsOf(img);
                    bad.Remove(name);
                    Evict(name, now);
                }
                else { Fail f = new Fail(); f.Stamp = stamp; f.CheckedAt = Now(); bad[name] = f; }
            }
            return img;
        }

        /// <summary>Forget one picture (it was replaced).</summary>
        public void Forget(string name)
        {
            lock (gate)
            {
                Item it;
                if (ok.TryGetValue(name, out it)) { pixels -= PixelsOf(it.Img); ok.Remove(name); }
                bad.Remove(name);
            }
        }

        /// <summary>Forget every picture (the folder may have changed).</summary>
        public void Clear() { lock (gate) { ok.Clear(); bad.Clear(); pixels = 0; } }
    }

    /// <summary>One style tile on the 外观 page.</summary>
    public sealed class StyleTile
    {
        public readonly string Id, Label, Hint;
        /// <summary>0 = 推荐（中心留空）, 1 = 经典, 2 = 自定义.</summary>
        public readonly int Group;
        /// <summary>The settings that matter for this style, in display order (see StyleShapes.Param*).</summary>
        public readonly string[] Params;

        public StyleTile(string id, string label, int group, string hint, params string[] prm)
        {
            Id = id; Label = label; Group = group; Hint = hint; Params = prm;
        }

        public bool Has(string param) { return Array.IndexOf(Params, param) >= 0; }
    }

    /// <summary>Primitive of a shape, in shape space (screen y down, centre at 0,0).</summary>
    internal struct ShapePrim
    {
        public int Type;          // 0 = box segment (square ends), 1 = capsule segment (round ends), 2 = dot, 3 = arc
        public double Ax, Ay, Bx, By; // segment ends / dot centre (A)
        public double R;          // dot radius / arc radius
        public double H;          // half thickness (segments, arcs)
        public double A0, Hs;     // arc centre angle and half span (radians); Hs >= PI = full circle
        // v4.1 (REBRUTE41 P2-4): segment constants, computed once in Build with exactly the expressions Sd used per pixel
        public double Vx, Vy, Len, Ux, Uy, Mx, My;

        /// <summary>Fills the segment constants (types 0 and 1).</summary>
        public void Prepare()
        {
            Vx = Bx - Ax; Vy = By - Ay;
            Len = Math.Sqrt(Vx * Vx + Vy * Vy);
            Ux = Vx / Len; Uy = Vy / Len;
            Mx = (Ax + Bx) / 2.0; My = (Ay + By) / 2.0;
        }
    }

    public static class StyleShapes
    {
        // ---------------- kinds & tiles ----------------

        /// <summary>Every new Kind (CrosshairStyle.Kind); "" (not listed) = the v3 shapes.</summary>
        public static readonly string[] Kinds = new string[]
        {
            "chevron", "chevron2", "xcross", "hbars", "corners", "brackets", "dotring", "arcs", "smile",
            "fourdots", "ringticks", "diamond", "triangle", "custom"
        };

        public const string ParamSize = "size", ParamThick = "thick", ParamGap = "gap", ParamLen = "len",
            ParamSpread = "spread", ParamCount = "count", ParamCrossGap = "xgap", ParamCrossLen = "xlen",
            ParamCrossThick = "xthick", ParamDashes = "dashes", ParamDashGap = "dashgap", ParamPng = "png";

        public static readonly string[] GroupNames = new string[] { "推荐 · 中心留空", "经典", "自定义" };

        public static readonly StyleTile[] Tiles = new StyleTile[]
        {
            new StyleTile("chevron", "宽距三角", 0, "向上的箭头，没有底边，整体下沉到中心下面，中间完全空出来", ParamGap, ParamLen, ParamSpread, ParamThick),
            new StyleTile("chevron2", "双层箭头", 0, "两道向上的箭头，叠在中心下面", ParamGap, ParamLen, ParamSpread, ParamThick),
            new StyleTile("cross", "中空十字", 0, "经典十字，中间空着", ParamCrossGap, ParamCrossLen, ParamCrossThick),
            new StyleTile("tee", "T 形（无上）", 0, "十字去掉上面一笔，上方视野更干净", ParamCrossGap, ParamCrossLen, ParamCrossThick),
            new StyleTile("xcross", "斜十字", 0, "× 形四条斜线，中间空着", ParamGap, ParamLen, ParamThick),
            new StyleTile("hbars", "左右横线", 0, "只有左右两笔，上下视野不挡", ParamGap, ParamLen, ParamThick),
            new StyleTile("corners", "四角框", 0, "方框只留四个角", ParamSize, ParamLen, ParamThick),
            new StyleTile("brackets", "括号 [ ]", 0, "左右两个括号把中心夹住", ParamSize, ParamLen, ParamThick),
            new StyleTile("dotring", "点阵圆环", 0, "一圈小圆点", ParamSize, ParamCount, ParamThick),
            new StyleTile("arcs", "分段圆弧", 0, "几段圆弧围成一圈", ParamSize, ParamCount, ParamSpread, ParamThick),
            new StyleTile("smile", "下半弧", 0, "一道托在下面的弧线", ParamSize, ParamSpread, ParamThick),
            new StyleTile("fourdots", "四点", 0, "上下左右四个小点", ParamGap, ParamThick),
            new StyleTile("ringticks", "圆环 + 刻度", 0, "圆环外面再加四个短刻度", ParamSize, ParamLen, ParamThick),
            new StyleTile("diamond", "空心菱形", 0, "菱形边框", ParamSize, ParamThick),
            new StyleTile("triangle", "空心三角", 0, "三角形边框，中心在里面空着", ParamSize, ParamThick),
            new StyleTile("ring", "经典圆环", 1, "默认样式", ParamSize, ParamThick),
            new StyleTile("dashring", "虚线圆环", 1, "圆环分成几段", ParamSize, ParamThick, ParamDashes, ParamDashGap),
            new StyleTile("square", "方框", 1, "正方形边框", ParamSize, ParamThick),
            new StyleTile("ringcross", "圆环 + 十字", 1, "圆环外面加十字", ParamSize, ParamThick, ParamCrossGap, ParamCrossLen, ParamCrossThick),
            new StyleTile("custom", "自定义图片", 2, "上传自己的 PNG，中间会自动挖空", ParamSize, ParamPng),
        };

        public static StyleTile FindTile(string id)
        {
            foreach (StyleTile t in Tiles) if (t.Id == id) return t;
            return null;
        }

        /// <summary>The tile that shows the profile's current look, or null (e.g. the v3 实心点).</summary>
        public static StyleTile TileOf(Profile p)
        {
            if (p == null) return null;
            CrosshairStyle s = Of(p);
            if (!string.IsNullOrEmpty(s.Kind)) return FindTile(s.Kind);
            switch (p.Shape)
            {
                case "ring": return FindTile(p.Dashes >= 2 ? "dashring" : "ring");
                case "square": return FindTile("square");
                case "ringcross": return FindTile("ringcross");
                case "cross": return FindTile(p.CrossTop ? "cross" : "tee");
            }
            return null;
        }

        /// <summary>
        /// Switches the profile to a tile: sets that style's shape values to good defaults (colours, opacity, outline,
        /// glow, 换手颜色 and the 强化 ratio are kept). Returns false for an unknown id.
        /// </summary>
        public static bool ApplyTile(Profile p, string id)
        {
            StyleTile t = FindTile(id);
            if (p == null || t == null) return false;
            CrosshairStyle s = Of(p);
            double oldD = p.Diameter;
            double boostRatio = p.Boost != null && oldD > 0 ? p.Boost.Diameter / oldD : 1.25;
            if (double.IsNaN(boostRatio) || boostRatio < 0.5) boostRatio = 0.5;
            if (boostRatio > 3) boostRatio = 3;
            p.RotationDeg = 0;
            p.CenterDot = false;
            s.Hole = Math.Max(CrosshairStyle.MinHole, s.Hole);
            bool legacy = true;
            switch (id)
            {
                case "ring": p.Shape = "ring"; p.Dashes = 0; p.Diameter = 12; p.Thickness = 1.2; break;
                case "dashring": p.Shape = "ring"; p.Dashes = 8; p.DashGapPct = 35; p.Diameter = 14; p.Thickness = 1.4; break;
                case "square": p.Shape = "square"; p.Dashes = 0; p.Diameter = 12; p.Thickness = 1.2; break;
                case "ringcross":
                    p.Shape = "ringcross"; p.Dashes = 0; p.Diameter = 12; p.Thickness = 1.2;
                    p.CrossGap = 7.5; p.CrossLength = 4; p.CrossThickness = 1.2; p.CrossTop = true; break;
                case "cross": p.Shape = "cross"; p.CrossGap = 4; p.CrossLength = 6; p.CrossThickness = 1.5; p.CrossTop = true; break;
                case "tee": p.Shape = "cross"; p.CrossGap = 4; p.CrossLength = 6; p.CrossThickness = 1.5; p.CrossTop = false; break;
                default: legacy = false; break;
            }
            if (legacy) { s.Kind = ""; }
            else
            {
                s.Kind = id;
                switch (id)
                {
                    case "chevron": s.Gap = 4; s.Length = 7; s.Spread = 120; p.Thickness = 1.4; break;
                    case "chevron2": s.Gap = 4; s.Length = 6; s.Spread = 120; p.Thickness = 1.3; break;
                    case "xcross": s.Gap = 4; s.Length = 5; p.Thickness = 1.4; break;
                    case "hbars": s.Gap = 5; s.Length = 7; p.Thickness = 1.4; break;
                    case "corners": p.Diameter = 16; s.Length = 4; p.Thickness = 1.4; break;
                    case "brackets": p.Diameter = 14; s.Length = 3; p.Thickness = 1.4; break;
                    case "dotring": p.Diameter = 14; s.Count = 8; p.Thickness = 2; break;
                    case "arcs": p.Diameter = 14; s.Count = 3; s.Spread = 70; p.Thickness = 1.4; break;
                    case "smile": p.Diameter = 14; s.Spread = 140; p.Thickness = 1.4; break;
                    case "fourdots": s.Gap = 6; p.Thickness = 2.2; break;
                    case "ringticks": p.Diameter = 12; s.Length = 4; p.Thickness = 1.2; break;
                    case "diamond": p.Diameter = 16; p.Thickness = 1.4; break;
                    case "triangle": p.Diameter = 18; p.Thickness = 1.4; break;
                    case "custom": p.Diameter = 32; break;
                }
            }
            if (p.Boost != null && p.Diameter != oldD)
                p.Boost.Diameter = Math.Max(4, Math.Min(300, Math.Round(p.Diameter * boostRatio * 2) / 2));
            return true;
        }

        // ---------------- Profile.Style access ----------------

        private static readonly object fieldLock = new object();
        private static bool looked;
        private static FieldInfo styleField;
        private static PropertyInfo styleProp;
        private static readonly ConditionalWeakTable<Profile, CrosshairStyle> side = new ConditionalWeakTable<Profile, CrosshairStyle>();

        private static void Look()
        {
            if (looked) return;
            lock (fieldLock)
            {
                if (looked) return;
                FieldInfo f = typeof(Profile).GetField("Style", BindingFlags.Public | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(CrosshairStyle)) styleField = f;
                else
                {
                    PropertyInfo pi = typeof(Profile).GetProperty("Style", BindingFlags.Public | BindingFlags.Instance);
                    if (pi != null && pi.PropertyType == typeof(CrosshairStyle) && pi.CanRead && pi.CanWrite) styleProp = pi;
                }
                looked = true;
            }
        }

        /// <summary>
        /// The profile's style data (Profile.Style, created when missing). Until CORE adds that field (request
        /// v4-requests/STYLE.md) a per-object side table keeps it for this session only (not saved, not cloned).
        /// </summary>
        public static CrosshairStyle Of(Profile p)
        {
            if (p == null) return new CrosshairStyle();
            Look();
            if (styleField != null)
            {
                CrosshairStyle s = styleField.GetValue(p) as CrosshairStyle;
                if (s == null) { s = new CrosshairStyle(); styleField.SetValue(p, s); }
                return s;
            }
            if (styleProp != null)
            {
                CrosshairStyle s = styleProp.GetValue(p, null) as CrosshairStyle;
                if (s == null) { s = new CrosshairStyle(); styleProp.SetValue(p, s, null); }
                return s;
            }
            return side.GetValue(p, delegate(Profile k) { return new CrosshairStyle(); });
        }

        /// <summary>Replaces the profile's style data (used after Profile.Clone so a copy never shares it).</summary>
        public static void SetStyle(Profile p, CrosshairStyle s)
        {
            if (p == null) return;
            if (s == null) s = new CrosshairStyle();
            Look();
            if (styleField != null) { styleField.SetValue(p, s); return; }
            if (styleProp != null) { styleProp.SetValue(p, s, null); return; }
            side.Remove(p);
            side.Add(p, s);
        }

        /// <summary>Profile.Clone with its own copy of the style data.</summary>
        public static Profile CloneWithStyle(Profile p)
        {
            Profile c = p.Clone();
            SetStyle(c, Of(p).Clone());
            return c;
        }

        /// <summary>True once Profile has a saved Style field (styles survive a restart / profile clone).</summary>
        public static bool StyleIsSaved { get { Look(); return styleField != null || styleProp != null; } }

        // ---------------- 自定义图片 ----------------

        /// <summary>Set by CustomCrosshair (Windows only): file name -> decoded picture, or null when missing/broken.</summary>
        public static Func<string, PngImage> PngProvider;

        public static PngImage Png(string file)
        {
            Func<string, PngImage> f = PngProvider;
            if (f == null || string.IsNullOrEmpty(file)) return null;
            try { return f(file); } catch (Exception) { return null; }
        }

        /// <summary>Longest file name kept, in UTF-16 units.</summary>
        public const int MaxFileName = 120;

        /// <summary>
        /// File name only (no folders), without characters Windows does not allow; "" when nothing is left.
        /// v4.1 (BRUTE40 P2-7): lone UTF-16 surrogates are dropped and the 120-unit tail cut never splits an emoji.
        /// </summary>
        public static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int cut = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            if (cut >= 0) name = name.Substring(cut + 1);
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0) continue;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < name.Length && char.IsLowSurrogate(name[i + 1])) { b.Append(c).Append(name[i + 1]); i++; }
                    continue;
                }
                if (char.IsLowSurrogate(c)) continue; // a lone half of a pair
                b.Append(c);
            }
            string r = TrimName(b.ToString());
            if (r == "." || r == "..") r = "";
            if (r.Length > MaxFileName)
            {
                int start = r.Length - MaxFileName;
                if (char.IsLowSurrogate(r[start])) start++; // keep whole pairs
                r = TrimName(r.Substring(start));
            }
            return r;
        }

        /// <summary>Spaces around and dots at the end removed until nothing changes ("a ." -> "a"), so SafeFileName is stable.</summary>
        private static string TrimName(string r)
        {
            string prev;
            do { prev = r; r = r.Trim().TrimEnd('.'); } while (r != prev);
            return r;
        }

        /// <summary>v4.1 (BRUTE40 P2-7): the first max UTF-16 units of s, one less when the cut would split a pair.</summary>
        public static string CutChars(string s, int max)
        {
            if (s == null) return "";
            if (max <= 0) return "";
            if (s.Length <= max) return s;
            int n = max;
            if (char.IsHighSurrogate(s[n - 1])) n--;
            return s.Substring(0, n);
        }

        /// <summary>
        /// v4.1 (BRUTE40 P2-8): width and height from a PNG file's first 24 bytes (signature + IHDR), so a picture that is
        /// far too big is refused before it is decoded. False when the bytes are not a PNG header.
        /// </summary>
        public static bool ReadPngSize(byte[] head, out int width, out int height)
        {
            width = height = 0;
            if (head == null || head.Length < 24) return false;
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int i = 0; i < 8; i++) if (head[i] != sig[i]) return false;
            if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R') return false;
            long w = ((long)head[16] << 24) | ((long)head[17] << 16) | ((long)head[18] << 8) | head[19];
            long h = ((long)head[20] << 24) | ((long)head[21] << 16) | ((long)head[22] << 8) | head[23];
            if (w < 1 || h < 1 || w > int.MaxValue || h > int.MaxValue) return false;
            width = (int)w; height = (int)h;
            return true;
        }

        // ---------------- geometry ----------------

        [ThreadStatic] private static List<ShapePrim> scratch;

        private static List<ShapePrim> Scratch(RingStyle s)
        {
            List<ShapePrim> l = scratch;
            if (l == null) { l = new List<ShapePrim>(32); scratch = l; }
            l.Clear();
            Build(s, l);
            return l;
        }

        /// <summary>v4.1: Extent(Build(s)) without allocating (per-frame CanvasSize; one list per thread).</summary>
        internal static double ExtentOf(RingStyle s) { return Extent(Scratch(s)); }

        /// <summary>v4.1: farthest point of the shape's primitives (true distance, no outline / feather / glow), px.</summary>
        internal static double CoreReachOf(RingStyle s)
        {
            List<ShapePrim> prims = Scratch(s);
            double e = 0;
            for (int i = 0; i < prims.Count; i++)
            {
                ShapePrim p = prims[i];
                double x;
                if (p.Type == 2) x = Math.Sqrt(p.Ax * p.Ax + p.Ay * p.Ay) + p.R;
                else if (p.Type == 3) x = p.R + p.H;
                else if (p.Type == 1) x = Math.Max(Math.Sqrt(p.Ax * p.Ax + p.Ay * p.Ay), Math.Sqrt(p.Bx * p.Bx + p.By * p.By)) + p.H;
                else
                {
                    // box: the farthest of its four corners (A ± n·H, B ± n·H)
                    double vx = p.Bx - p.Ax, vy = p.By - p.Ay, len = Math.Sqrt(vx * vx + vy * vy);
                    double nx = len < 1e-9 ? 0 : -vy / len * p.H, ny = len < 1e-9 ? p.H : vx / len * p.H;
                    x = Math.Max(Math.Max(Hyp(p.Ax + nx, p.Ay + ny), Hyp(p.Ax - nx, p.Ay - ny)),
                                 Math.Max(Hyp(p.Bx + nx, p.By + ny), Hyp(p.Bx - nx, p.By - ny)));
                }
                if (x > e) e = x;
            }
            return e;
        }

        private static double Hyp(double x, double y) { return Math.Sqrt(x * x + y * y); }

        internal static List<ShapePrim> Build(RingStyle s)
        {
            List<ShapePrim> l = new List<ShapePrim>();
            Build(s, l);
            return l;
        }

        internal static void Build(RingStyle s, List<ShapePrim> l)
        {
            double R = Math.Max(s.Diameter / 2.0, 0.5), T = Math.Max(s.Thickness, 0.3), h = T / 2.0;
            double G = Math.Max(s.SGap, 0), L = Math.Max(s.SLength, 0.3);
            int N = s.SCount < 2 ? 2 : (s.SCount > 24 ? 24 : s.SCount);
            switch (s.Kind)
            {
                case "chevron":
                case "chevron2":
                    {
                        double half = Clamp(s.SSpread, 20, 170) / 2.0 * Math.PI / 180.0;
                        double sx = Math.Sin(half) * L, sy = Math.Cos(half) * L;
                        double ay = G + h; // the apex sits below the centre (screen y down): the centre stays empty
                        Cap(l, 0, ay, -sx, ay + sy, h); Cap(l, 0, ay, sx, ay + sy, h);
                        if (s.Kind == "chevron2")
                        {
                            double a2 = ay + Math.Max(2.5, 2.2 * T + 1.5);
                            Cap(l, 0, a2, -sx, a2 + sy, h); Cap(l, 0, a2, sx, a2 + sy, h);
                        }
                        break;
                    }
                case "xcross":
                    {
                        double k = 1.0 / Math.Sqrt(2.0), g0 = G + h;
                        for (int i = 0; i < 4; i++)
                        {
                            double dx = (i == 0 || i == 3) ? k : -k, dy = i < 2 ? k : -k;
                            Box(l, dx * g0, dy * g0, dx * (g0 + L), dy * (g0 + L), h);
                        }
                        break;
                    }
                case "hbars":
                    Box(l, G, 0, G + L, 0, h); Box(l, -G, 0, -G - L, 0, h);
                    break;
                case "corners":
                    {
                        double len = Math.Min(L, R);
                        for (int i = 0; i < 4; i++)
                        {
                            double sx = (i & 1) == 0 ? 1 : -1, sy = (i & 2) == 0 ? 1 : -1;
                            Box(l, sx * (R + h), sy * R, sx * (R - len), sy * R, h);
                            Box(l, sx * R, sy * (R + h), sx * R, sy * (R - len), h);
                        }
                        break;
                    }
                case "brackets":
                    {
                        double len = Math.Min(L, R);
                        for (int i = 0; i < 2; i++)
                        {
                            double sx = i == 0 ? 1 : -1;
                            Box(l, sx * R, -(R + h), sx * R, R + h, h);
                            Box(l, sx * R, -R, sx * (R - len), -R, h);
                            Box(l, sx * R, R, sx * (R - len), R, h);
                        }
                        break;
                    }
                case "dotring":
                    for (int i = 0; i < N; i++)
                    {
                        double a = -Math.PI / 2.0 + i * 2.0 * Math.PI / N;
                        Dot(l, R * Math.Cos(a), R * Math.Sin(a), h);
                    }
                    break;
                case "arcs":
                    {
                        double per = 360.0 / N;
                        double span = Clamp(s.SSpread, 5, Math.Max(5, per - 6)) / 2.0 * Math.PI / 180.0;
                        for (int i = 0; i < N; i++) Arc(l, R, h, -Math.PI / 2.0 + i * 2.0 * Math.PI / N, span);
                        break;
                    }
                case "smile":
                    Arc(l, R, h, Math.PI / 2.0, Clamp(s.SSpread, 20, 330) / 2.0 * Math.PI / 180.0);
                    break;
                case "fourdots":
                    {
                        double g0 = G + h;
                        Dot(l, g0, 0, h); Dot(l, -g0, 0, h); Dot(l, 0, g0, h); Dot(l, 0, -g0, h);
                        break;
                    }
                case "ringticks":
                    Arc(l, R, h, 0, Math.PI);
                    Box(l, R, 0, R + h + L, 0, h); Box(l, -R, 0, -R - h - L, 0, h);
                    Box(l, 0, R, 0, R + h + L, h); Box(l, 0, -R, 0, -R - h - L, h);
                    break;
                case "diamond":
                    Cap(l, 0, -R, R, 0, h); Cap(l, R, 0, 0, R, h); Cap(l, 0, R, -R, 0, h); Cap(l, -R, 0, 0, -R, h);
                    break;
                case "triangle":
                    for (int i = 0; i < 3; i++) // no arrays: Build runs every frame (CanvasSize / CoreReach)
                    {
                        double a = -Math.PI / 2.0 + i * 2.0 * Math.PI / 3.0, b2 = -Math.PI / 2.0 + ((i + 1) % 3) * 2.0 * Math.PI / 3.0;
                        Cap(l, R * Math.Cos(a), R * Math.Sin(a), R * Math.Cos(b2), R * Math.Sin(b2), h);
                    }
                    break;
            }
        }

        private static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            return v < min ? min : (v > max ? max : v);
        }

        private static void Box(List<ShapePrim> l, double ax, double ay, double bx, double by, double h)
        {
            ShapePrim p = new ShapePrim(); p.Type = 0; p.Ax = ax; p.Ay = ay; p.Bx = bx; p.By = by; p.H = h; p.Prepare(); l.Add(p);
        }

        private static void Cap(List<ShapePrim> l, double ax, double ay, double bx, double by, double h)
        {
            ShapePrim p = new ShapePrim(); p.Type = 1; p.Ax = ax; p.Ay = ay; p.Bx = bx; p.By = by; p.H = h; p.Prepare(); l.Add(p);
        }

        private static void Dot(List<ShapePrim> l, double x, double y, double r)
        {
            ShapePrim p = new ShapePrim(); p.Type = 2; p.Ax = x; p.Ay = y; p.R = r; l.Add(p);
        }

        private static void Arc(List<ShapePrim> l, double r, double h, double a0, double hs)
        {
            ShapePrim p = new ShapePrim(); p.Type = 3; p.R = r; p.H = h; p.A0 = a0; p.Hs = hs; l.Add(p);
        }

        /// <summary>Farthest any primitive reaches from the centre (before outline / feather / glow).</summary>
        internal static double Extent(List<ShapePrim> prims)
        {
            double e = 0;
            foreach (ShapePrim p in prims)
            {
                double x;
                if (p.Type == 2) x = Math.Sqrt(p.Ax * p.Ax + p.Ay * p.Ay) + p.R;
                else if (p.Type == 3) x = p.R + p.H;
                else
                {
                    double a = Math.Sqrt(p.Ax * p.Ax + p.Ay * p.Ay), b = Math.Sqrt(p.Bx * p.Bx + p.By * p.By);
                    x = Math.Max(a, b) + p.H * (p.Type == 0 ? Math.Sqrt(2.0) : 1.0);
                }
                if (x > e) e = x;
            }
            return e;
        }

        /// <summary>Signed distance (negative inside) from (x, y) to the union of the primitives.</summary>
        internal static double Sd(List<ShapePrim> prims, double x, double y)
        {
            double best = double.MaxValue;
            for (int i = 0; i < prims.Count; i++)
            {
                ShapePrim p = prims[i];
                double d = SdPrim(ref p, x, y);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>v4.1: Sd over the first n primitives of an array (no struct copies; the renderer's per-pixel call).</summary>
        internal static double Sd(ShapePrim[] prims, int n, double x, double y)
        {
            double best = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                double d = SdPrim(ref prims[i], x, y);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Signed distance (negative inside) from (x, y) to one primitive; 1-Lipschitz (a true distance minus the half
        /// thickness, or a box SDF), which the tile skip of RingRaster relies on. Segments use the constants of Prepare.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double SdPrim(ref ShapePrim p, double x, double y)
        {
            if (p.Type == 2)
            {
                double dx = x - p.Ax, dy = y - p.Ay;
                return Math.Sqrt(dx * dx + dy * dy) - p.R;
            }
            if (p.Type == 3)
            {
                double r = Math.Sqrt(x * x + y * y);
                if (p.Hs >= Math.PI) return Math.Abs(r - p.R) - p.H;
                double diff = Math.Atan2(y, x) - p.A0;
                while (diff > Math.PI) diff -= 2.0 * Math.PI;
                while (diff < -Math.PI) diff += 2.0 * Math.PI;
                if (Math.Abs(diff) <= p.Hs) return Math.Abs(r - p.R) - p.H;
                double ea = p.A0 + (diff > 0 ? p.Hs : -p.Hs);
                double ex = x - p.R * Math.Cos(ea), ey = y - p.R * Math.Sin(ea);
                return Math.Sqrt(ex * ex + ey * ey) - p.H; // round end
            }
            double len = p.Len;
            if (len < 1e-9) { double dx = x - p.Ax, dy = y - p.Ay; return Math.Sqrt(dx * dx + dy * dy) - p.H; }
            if (p.Type == 1)
            {
                double vx = p.Vx, vy = p.Vy;
                double t = ((x - p.Ax) * vx + (y - p.Ay) * vy) / (len * len);
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double dx = x - (p.Ax + vx * t), dy = y - (p.Ay + vy * t);
                return Math.Sqrt(dx * dx + dy * dy) - p.H;
            }
            {
                double ux = p.Ux, uy = p.Uy;
                double mx = p.Mx, my = p.My;
                double u = (x - mx) * ux + (y - my) * uy, v = -(x - mx) * uy + (y - my) * ux;
                double qx = Math.Abs(u) - len / 2.0, qy = Math.Abs(v) - p.H;
                double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0);
            }
        }

        // ---------------- thumbnails ----------------

        /// <summary>A copy of the look with every size multiplied by f (feather kept, so a big thumbnail stays sharp).</summary>
        public static RingStyle Scaled(RingStyle s, double f)
        {
            RingStyle r = s.Copy();
            r.Diameter *= f; r.Thickness *= f; r.OuterThickness *= f; r.CenterDotSize *= f;
            r.CrossLength *= f; r.CrossThickness *= f; r.CrossGap *= f; r.GlowPx *= f;
            r.SGap *= f; r.SLength *= f; r.Hole *= f;
            return r;
        }
    }
}
