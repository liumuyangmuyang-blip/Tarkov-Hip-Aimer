// v4.4 (W9) NET: download + verify + swap the exe. Pure file work (no WinForms); the restart itself is in Net.UpdateUi.cs.
// Order: download the zip into <user folder>\update-download, check its SHA-256 against the release's "sha256:" line,
// take the one TarkovHipAimer exe out of the zip as <exe>.new (MZ header + CRC checked), rename the running exe to
// <exe>.old (Windows allows renaming a running exe), move <exe>.new to <exe>. Any failure: the old exe is put back.
// settings.json, net-prefs.json and 准星样式 Crosshairs are never touched. CleanupAfterUpdate (next start) deletes .old/.new
// and the download folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TarkovCrosshair
{
    /// <summary>Minimal zip reader (central directory, stored + deflate, CRC-32 checked). Only what the updater needs.</summary>
    internal sealed class ZipMini
    {
        internal sealed class Entry
        {
            public string Name;
            public int Method;
            public uint Crc;
            public long CompSize, Size, LocalOffset;
        }

        public readonly List<Entry> Entries = new List<Entry>();
        private readonly byte[] data;

        public const long MaxEntryBytes = 64L * 1024 * 1024;

        public ZipMini(byte[] zip)
        {
            data = zip;
            int eocd = -1;
            for (int i = zip.Length - 22; i >= Math.Max(0, zip.Length - 22 - 65535); i--)
                if (U32(i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) throw new InvalidDataException("not a zip");
            int count = U16(eocd + 10);
            long cd = U32(eocd + 16);
            int p = (int)cd;
            for (int k = 0; k < count; k++)
            {
                if (p + 46 > zip.Length || U32(p) != 0x02014b50) throw new InvalidDataException("bad central directory");
                int flags = U16(p + 8);
                Entry e = new Entry();
                e.Method = U16(p + 10);
                e.Crc = U32(p + 16);
                e.CompSize = U32(p + 20);
                e.Size = U32(p + 24);
                int nl = U16(p + 28), xl = U16(p + 30), cl = U16(p + 32);
                e.LocalOffset = U32(p + 42);
                if (p + 46 + nl > zip.Length) throw new InvalidDataException("bad name");
                Encoding enc = (flags & 0x800) != 0 ? Encoding.UTF8 : LegacyNames();
                e.Name = enc.GetString(zip, p + 46, nl);
                Entries.Add(e);
                p += 46 + nl + xl + cl;
            }
        }

        /// <summary>Names without the UTF-8 flag: code page 437 where available, else ASCII (the exe name is ASCII-matched).</summary>
        private static Encoding LegacyNames()
        {
            try { return Encoding.GetEncoding(437); } catch (Exception) { return Encoding.ASCII; }
        }

        private int U16(long i) { return data[i] | (data[i + 1] << 8); }
        private uint U32(long i) { return (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24)); }

        public byte[] Read(Entry e)
        {
            if (e.Size > MaxEntryBytes || e.CompSize > data.Length) throw new InvalidDataException("entry too large");
            long lh = e.LocalOffset;
            if (lh + 30 > data.Length || U32(lh) != 0x04034b50) throw new InvalidDataException("bad local header");
            long start = lh + 30 + U16(lh + 26) + U16(lh + 28);
            if (start + e.CompSize > data.Length) throw new InvalidDataException("truncated");
            byte[] outb;
            if (e.Method == 0)
            {
                outb = new byte[e.Size];
                Buffer.BlockCopy(data, (int)start, outb, 0, (int)e.Size);
            }
            else if (e.Method == 8)
            {
                outb = new byte[e.Size];
                using (MemoryStream ms = new MemoryStream(data, (int)start, (int)e.CompSize))
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    int got = 0, n;
                    while (got < outb.Length && (n = ds.Read(outb, got, outb.Length - got)) > 0) got += n;
                    if (got != outb.Length) throw new InvalidDataException("short entry");
                }
            }
            else throw new InvalidDataException("method " + e.Method);
            if (Crc32(outb) != e.Crc) throw new InvalidDataException("crc");
            return outb;
        }

        private static uint[] table;
        public static uint Crc32(byte[] b)
        {
            if (table == null)
            {
                uint[] t = new uint[256];
                for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; }
                table = t;
            }
            uint crc = 0xFFFFFFFFu;
            foreach (byte x in b) crc = table[(crc ^ x) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }

    internal enum InstallStep { Ok, Download, Hash, Zip, Swap }

    internal sealed class InstallResult
    {
        public InstallStep Step;
        public string Detail;       // for the log only
        public bool Ok { get { return Step == InstallStep.Ok; } }

        /// <summary>Plain Chinese message for the user (shown after a failure).</summary>
        public string UserMessage
        {
            get
            {
                switch (Step)
                {
                    case InstallStep.Ok: return "更新好了，正在重新打开。";
                    case InstallStep.Download: return "更新没下载下来（网络断了或太慢）。旧版本照常能用，下次打开会再提示。";
                    case InstallStep.Hash: return "下载的更新文件校验不对，没有换上。旧版本照常能用。";
                    case InstallStep.Zip: return "更新包里没找到程序文件，没有换上。旧版本照常能用。";
                    default: return "更新没换上（程序文件被占用或文件夹不能写）。旧版本照常能用。也可以到下载页手动下载新版。";
                }
            }
        }
    }

    internal static class UpdateInstaller
    {
        public const string DownloadFolderName = "update-download";
        public const string RestartArg = "--updated";

        public static string OldPath(string exePath) { return exePath + ".old"; }
        public static string NewPath(string exePath) { return exePath + ".new"; }

        /// <summary>Blocking (background thread). userDir = SettingsIO.UserDir(); exePath = Application.ExecutablePath.</summary>
        public static InstallResult Install(IHttp http, ReleaseInfo r, string exePath, string userDir)
        {
            InstallResult res = new InstallResult();
            if (r == null || !r.Usable) { res.Step = InstallStep.Download; res.Detail = "release not usable"; return res; }
            string dir = Path.Combine(userDir, DownloadFolderName);
            string zipPath = Path.Combine(dir, "TarkovHipAimer-" + SafeName(r.Tag) + ".zip");
            try { Directory.CreateDirectory(dir); }
            catch (Exception e) { res.Step = InstallStep.Download; res.Detail = e.GetType().Name; return res; }

            string err;
            if (!http.Download(r.ZipUrl, zipPath, NetConfig.DownloadTimeoutMs, NetConfig.MaxZipBytes, out err))
            { res.Step = InstallStep.Download; res.Detail = err; return res; }
            if (!NetHash.Verify(zipPath, r.Sha256))
            { TryDelete(zipPath); res.Step = InstallStep.Hash; res.Detail = "sha256 mismatch"; return res; }

            string newPath = NewPath(exePath);
            try
            {
                ZipMini z = new ZipMini(File.ReadAllBytes(zipPath));
                ZipMini.Entry exe = FindExe(z);
                if (exe == null) { res.Step = InstallStep.Zip; res.Detail = "no exe entry"; return res; }
                byte[] bytes = z.Read(exe);
                if (bytes.Length < 1024 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z') { res.Step = InstallStep.Zip; res.Detail = "not an exe"; return res; }
                File.WriteAllBytes(newPath, bytes);
            }
            catch (Exception e) { TryDelete(newPath); res.Step = InstallStep.Zip; res.Detail = e.GetType().Name + ": " + e.Message; return res; }

            return Swap(exePath, newPath);
        }

        /// <summary>exe -> exe.old, exe.new -> exe; on failure the old exe is put back and exe.new removed.</summary>
        public static InstallResult Swap(string exePath, string newPath)
        {
            InstallResult res = new InstallResult();
            string oldPath = OldPath(exePath);
            bool movedOld = false;
            try
            {
                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(exePath, oldPath);
                movedOld = true;
                File.Move(newPath, exePath);
                res.Step = InstallStep.Ok;
                return res;
            }
            catch (Exception e)
            {
                res.Step = InstallStep.Swap; res.Detail = e.GetType().Name + ": " + e.Message;
                if (movedOld)
                {
                    try { if (!File.Exists(exePath)) File.Move(oldPath, exePath); } catch (Exception) { }
                }
                TryDelete(newPath);
                return res;
            }
        }

        /// <summary>The single entry named *TarkovHipAimer.exe (any folder in the zip). Null if none or several.</summary>
        public static ZipMini.Entry FindExe(ZipMini z)
        {
            ZipMini.Entry found = null;
            foreach (ZipMini.Entry e in z.Entries)
            {
                string n = e.Name.Replace('\\', '/');
                string leaf = n.Substring(n.LastIndexOf('/') + 1);
                if (!leaf.EndsWith("TarkovHipAimer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null) return null;
                found = e;
            }
            return found;
        }

        /// <summary>Next start: remove exe.old / exe.new and the download folder. Never throws; returns how many were removed.</summary>
        public static int CleanupAfterUpdate(string exePath, string userDir)
        {
            int n = 0;
            if (TryDelete(OldPath(exePath))) n++;
            if (TryDelete(NewPath(exePath))) n++;
            try
            {
                string dir = Path.Combine(userDir, DownloadFolderName);
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "TarkovHipAimer-*.zip")) if (TryDelete(f)) n++;
                    if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
                }
            }
            catch (Exception) { }
            return n;
        }

        private static bool TryDelete(string p)
        {
            try { if (File.Exists(p)) { File.Delete(p); return true; } } catch (Exception) { }
            return false;
        }

        private static string SafeName(string s)
        {
            StringBuilder b = new StringBuilder();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c) || c == '.') b.Append(c);
            return b.Length == 0 ? "new" : b.ToString();
        }
    }
}
