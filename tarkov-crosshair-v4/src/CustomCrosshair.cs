// v4.0 自定义准星图片 (Windows: System.Drawing). Pictures live in 「准星样式 Crosshairs」 next to settings.json.
// The centre hole is NOT baked into the file: RingRaster cuts it on every render (radius = 中心留空, at least 3 px),
// so the game's own centre dot always shows whatever picture is used.
// v4.1 (BRUTE40 P2-8/P2-9): the size is read from the PNG header before anything is decoded; the overlay thread never
// decodes (it gets PngImage.Loading while a thread-pool thread reads the picture); a picture that failed is read again
// once its file changes (PngCache in StyleShapes.cs, pure and tested).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TarkovCrosshair
{
    public static class CustomCrosshair
    {
        /// <summary>Largest picture file accepted (bytes).</summary>
        public const long MaxFileBytes = 16L * 1024 * 1024;
        /// <summary>Largest picture side accepted (px).</summary>
        public const int MaxSide = 8192;
        /// <summary>Pictures are kept in memory at most this big (the crosshair is at most 300 px).</summary>
        public const int CacheSide = 512;
        /// <summary>Longest base name of an uploaded picture (UTF-16 units, never splitting an emoji).</summary>
        public const int MaxBaseName = 60;

        private static int loads;
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly PngCache cache = MakeCache();
        [ThreadStatic] private static bool noBlock;

        private static PngCache MakeCache()
        {
            PngCache c = new PngCache();
            c.Stamp = StampOf;
            c.Decode = DecodeFile;
            c.NowMs = delegate { return clock.ElapsedMilliseconds; };
            c.Background = delegate(Action a) { ThreadPool.QueueUserWorkItem(delegate { a(); }); };
            return c;
        }

        /// <summary>Makes RingRaster able to draw 自定义图片 (idempotent; call before rendering).</summary>
        public static void Install()
        {
            StyleShapes.PngProvider = Get;
        }

        /// <summary>v4.1: the calling thread (the overlay's render thread) never decodes: a picture not read yet is read on
        /// the thread pool and drawn as PngImage.Loading (nothing) until it is ready.</summary>
        public static void NeverBlockThisThread() { noBlock = true; }

        public static string Folder()
        {
            return SettingsIO.CrosshairDir();
        }

        /// <summary>Creates the folder when missing. Returns false (with a message) when Windows refuses.</summary>
        public static bool EnsureFolder(out string error)
        {
            error = null;
            try { Directory.CreateDirectory(Folder()); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Decoded picture for a file name in the folder (cached), or null when it is missing or not a picture;
        /// on a NeverBlockThisThread thread PngImage.Loading while it is being read.</summary>
        public static PngImage Get(string fileName)
        {
            string name = StyleShapes.SafeFileName(fileName);
            if (name.Length == 0) return null;
            return cache.Get(name, !noBlock);
        }

        /// <summary>Forget every cached picture (the folder may have changed).</summary>
        public static void Invalidate()
        {
            cache.Clear();
        }

        /// <summary>
        /// v4.1 (Leo 17 Nov 2026, UI41F ask): call after the settings window is closed and disposed. Drops every picture
        /// only the settings previews needed; the one the overlay draws stays (PngCache.TrimUi). The cache is bounded anyway
        /// (PngCache.MaxPictures / MaxPixels). Returns how many pictures were dropped. Any thread.
        /// </summary>
        public static int TrimUiCaches()
        {
            return cache.TrimUi();
        }

        /// <summary>Picture files in the folder (name only), sorted.</summary>
        public static List<string> List()
        {
            List<string> l = new List<string>();
            try
            {
                string dir = Folder();
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*.png"))
                    {
                        string n = Path.GetFileName(f);
                        if (StyleShapes.SafeFileName(n) == n) l.Add(n);
                    }
            }
            catch (Exception) { }
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        /// <summary>
        /// 上传: checks the file is a readable PNG, copies it into the folder (a new name when one with that name already
        /// holds a different picture) and returns the stored file name, or null with a message for the user.
        /// </summary>
        public static string Import(string sourcePath, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) { error = "找不到这个文件。"; return null; }
                if (!string.Equals(Path.GetExtension(sourcePath), ".png", StringComparison.OrdinalIgnoreCase))
                { error = "请选择 PNG 图片（.png）。"; return null; }
                FileInfo fi = new FileInfo(sourcePath);
                if (fi.Length <= 0) { error = "这个文件是空的。"; return null; }
                if (fi.Length > MaxFileBytes) { error = "图片太大（最多 16 MB）。"; return null; }
                byte[] bytes = File.ReadAllBytes(sourcePath);
                int w, h;
                // v4.1: the size comes from the PNG header, before anything is decoded
                if (!StyleShapes.ReadPngSize(bytes, out w, out h)) { error = "这个文件不是能打开的 PNG 图片。"; return null; }
                if (w > MaxSide || h > MaxSide) { error = "图片尺寸不支持（最大 " + MaxSide + "×" + MaxSide + "）。"; return null; }
                // v5.0.2 (W16, BRUTE P3-5): decoded ONCE; the same picture goes into the cache below (was a second decode)
                PngImage checkedImg = DecodeBytes(bytes, "check");
                if (checkedImg == null) { error = "这个文件不是能打开的 PNG 图片。"; return null; }
                string dirErr;
                if (!EnsureFolder(out dirErr)) { error = "无法创建文件夹「" + SettingsIO.CrosshairFolderName + "」：" + dirErr; return null; }
                string dir = Folder();
                string full = Path.GetFullPath(sourcePath);
                // Already inside the folder: use it as it is.
                if (string.Equals(Path.GetDirectoryName(full).TrimEnd('\\', '/'), Path.GetFullPath(dir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                {
                    string own = Path.GetFileName(full);
                    if (StyleShapes.SafeFileName(own) == own) { cache.Forget(own); PutChecked(own, checkedImg); return own; }
                }
                string baseName = StyleShapes.SafeFileName(Path.GetFileNameWithoutExtension(sourcePath));
                if (baseName.Length == 0) baseName = "准星";
                baseName = StyleShapes.CutChars(baseName, MaxBaseName).Trim(); // v4.1: never half an emoji
                if (baseName.Length == 0) baseName = "准星";
                string name = baseName + ".png";
                for (int i = 2; File.Exists(Path.Combine(dir, name)); i++)
                {
                    if (SameBytes(Path.Combine(dir, name), bytes)) { cache.Forget(name); PutChecked(name, checkedImg); return name; }
                    name = baseName + " (" + i + ").png";
                    if (i > 999) { error = "文件夹里同名图片太多了。"; return null; }
                }
                File.WriteAllBytes(Path.Combine(dir, name), bytes);
                cache.Forget(name);
                if (!PutChecked(name, checkedImg)) { error = "图片复制好了，但读取失败。"; return null; }
                return name;
            }
            catch (Exception ex)
            {
                error = "上传失败：" + ex.Message;
                return null;
            }
        }

        /// <summary>v5.0.2 (W16, BRUTE P3-5): the upload's checked picture becomes the cached picture of 'name' (id from the
        /// stored file's stamp, like a normal load), so nothing is decoded twice. false when the stored file is gone.</summary>
        private static bool PutChecked(string name, PngImage img)
        {
            long stamp;
            try { stamp = StampOf(name); } catch (Exception) { return false; }
            if (stamp == long.MinValue || img == null) return false;
            cache.Put(name, new PngImage(img.Pix, img.W, img.H, name + "|" + stamp + "|" + Interlocked.Increment(ref loads)));
            return true;
        }

        /// <summary>
        /// v5.0.2 (W16, BRUTE P3-5): Import on a background thread, so a huge picture (8192 x 8192: seconds of decoding)
        /// never blocks the settings window. done(name, error) runs on that background thread: marshal it to the UI thread
        /// (Control.BeginInvoke). Same result as Import.
        /// </summary>
        public static void BeginImport(string sourcePath, Action<string, string> done)
        {
            Thread t = new Thread(delegate()
            {
                string err = null, name = null;
                try { name = Import(sourcePath, out err); }
                catch (Exception ex) { err = "上传失败：" + ex.Message; name = null; }
                if (done != null) { try { done(name, err); } catch (Exception) { } }
            });
            t.IsBackground = true;
            t.Name = "CrosshairImport";
            t.Start();
        }

        private static bool SameBytes(string a, byte[] b)
        {
            try
            {
                FileInfo fa = new FileInfo(a);
                if (fa.Length != b.Length) return false;
                byte[] x = File.ReadAllBytes(a);
                for (int i = 0; i < x.Length; i++) if (x[i] != b[i]) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Change stamp of a file in the folder (write time and length), long.MinValue when it does not exist.</summary>
        private static long StampOf(string name)
        {
            FileInfo fi = new FileInfo(Path.Combine(Folder(), name));
            if (!fi.Exists) return long.MinValue;
            unchecked { return fi.LastWriteTimeUtc.Ticks * 31 + fi.Length; }
        }

        /// <summary>Reads a picture of the folder (any thread). Null when it is missing, too big or not a PNG.</summary>
        private static PngImage DecodeFile(string name, long stamp)
        {
            string path = Path.Combine(Folder(), name);
            FileInfo fi = new FileInfo(path);
            if (!fi.Exists || fi.Length <= 0 || fi.Length > MaxFileBytes) return null;
            byte[] bytes = File.ReadAllBytes(path); // no lock on the file: the user may delete / replace it later
            return DecodeBytes(bytes, name + "|" + stamp + "|" + Interlocked.Increment(ref loads));
        }

        /// <summary>PNG bytes -> premultiplied picture (longer side at most CacheSide). Checks the header size first.</summary>
        private static PngImage DecodeBytes(byte[] bytes, string id)
        {
            int w0, h0;
            if (!StyleShapes.ReadPngSize(bytes, out w0, out h0) || w0 > MaxSide || h0 > MaxSide) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image img = Image.FromStream(ms, false, true))
                {
                    if (!ImageFormat.Png.Equals(img.RawFormat)) return null;
                    if (img.Width < 1 || img.Height < 1 || img.Width > MaxSide || img.Height > MaxSide) return null;
                    return Decode(img, id); // straight from the stream: no full-size copy of the picture
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Premultiplied ARGB copy, shrunk (high quality) so the longer side is at most CacheSide.</summary>
        private static PngImage Decode(Image b, string id)
        {
            int w = b.Width, h = b.Height;
            double f = Math.Min(1.0, CacheSide / (double)Math.Max(w, h));
            int W = Math.Max(1, (int)Math.Round(w * f)), H = Math.Max(1, (int)Math.Round(h * f));
            using (Bitmap dst = new Bitmap(W, H, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(dst))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = f < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the edges
                        g.DrawImage(b, new Rectangle(0, 0, W, H), 0, 0, w, h, GraphicsUnit.Pixel, ia);
                    }
                }
                int[] pix = new int[W * H];
                BitmapData d = dst.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    for (int y = 0; y < H; y++)
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), pix, y * W, W);
                }
                finally { dst.UnlockBits(d); }
                return new PngImage(pix, W, H, id);
            }
        }

        /// <summary>Premultiplied 0xAARRGGBB pixels -> a new 32bppPArgb bitmap (caller disposes).</summary>
        public static Bitmap ToBitmap(int[] pix, int w, int h)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppPArgb);
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(pix, y * w, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), w);
            }
            finally { bmp.UnlockBits(d); }
            return bmp;
        }
    }
}
