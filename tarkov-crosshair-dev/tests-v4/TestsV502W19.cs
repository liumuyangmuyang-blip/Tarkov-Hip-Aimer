// v5.0.2 (W19): runtime files in 「数据 Data」 next to the exe (SettingsIO.DataDirFor): fresh start, migration from the
// exe-folder and parent-folder (bat) layouts, read-only fallback, crosshair folder stays at the top level, import picker.
using System;
using System.IO;
using System.Text;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V502W19Init() { V502W19Tests(); }

        static string W19Root()
        {
            string b = Environment.GetEnvironmentVariable("KIROCREW_SCRATCH");
            if (string.IsNullOrEmpty(b)) b = Path.GetTempPath();
            string r = Path.Combine(b, "w19-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(r);
            return r;
        }

        static string W19Settings()
        {
            return SettingsIO.Serialize(G());
        }

        static void V502W19Tests()
        {
            Func<string, bool> noBat = delegate(string p) { return false; };

            // W19-1 fresh start: 数据 Data is created, the top level gets nothing
            {
                string root = W19Root();
                string note;
                string d = SettingsIO.DataDirFor(root, SettingsIO.UserDirFor(root, noBat, null), out note);
                bool made = d == Path.Combine(root, "数据 Data") && Directory.Exists(d);
                bool topEmpty = Directory.GetFiles(root).Length == 0 && Directory.GetDirectories(root).Length == 1;
                bool dataEmpty = Directory.GetFiles(d).Length == 0; // the write probe is removed
                // a second start: same folder, still nothing at the top level
                string note2;
                bool again = SettingsIO.DataDirFor(root, root, out note2) == d && note2 == null && Directory.GetFiles(root).Length == 0;
                Check("W19-1 fresh start creates 「数据 Data」 next to the exe, writes nothing at the top level", made && topEmpty && dataEmpty && note == null && again,
                    "made=" + made + " top=" + topEmpty + " data=" + dataEmpty + " note=" + note + " again=" + again);
            }

            // W19-2 migration from the exe-folder layout (old files next to the exe)
            {
                string root = W19Root();
                string[] ours = { "settings.json", "crash.log", "crash.old.log", "shade-settings.json", "shade-recovery.json",
                    "shade-recovery.json.bak", "shade-recovery.bad-20261101-101010.json", "shade-engine.lock", "filter-prefs.json",
                    "net-prefs.json", "settings.before-import-20261101-101010.json", "settings.json.tmp", "filter-prefs.json.bad",
                    "ui-advanced.txt" };
                foreach (string n in ours) File.WriteAllText(Path.Combine(root, n), "old " + n);
                File.WriteAllText(Path.Combine(root, "settings.json"), W19Settings());
                string[] foreign = { "notes.txt", "other.tmp", "x.bad", "TarkovHipAimer.exe", "说明 README.txt" };
                foreach (string n in foreign) File.WriteAllText(Path.Combine(root, n), "keep");
                Directory.CreateDirectory(Path.Combine(root, "准星样式 Crosshairs"));
                string note;
                string d = SettingsIO.DataDirFor(root, root, out note);
                bool allCopied = true, allRenamed = true;
                foreach (string n in ours)
                {
                    if (!File.Exists(Path.Combine(d, n))) allCopied = false;
                    if (File.Exists(Path.Combine(root, n)) || !File.Exists(Path.Combine(root, n + ".old"))) allRenamed = false;
                }
                bool content = File.ReadAllText(Path.Combine(d, "crash.log")) == "old crash.log"
                    && SettingsIO.LooksLikeCrosshairSettings(File.ReadAllText(Path.Combine(d, "settings.json")));
                bool foreignKept = true;
                foreach (string n in foreign) if (!File.Exists(Path.Combine(root, n)) || File.Exists(Path.Combine(d, n))) foreignKept = false;
                bool crossKept = Directory.Exists(Path.Combine(root, "准星样式 Crosshairs")) && !Directory.Exists(Path.Combine(d, "准星样式 Crosshairs"));
                // second start: nothing moves again (*.old is never migrated)
                string note2;
                SettingsIO.DataDirFor(root, root, out note2);
                bool noOldInData = !File.Exists(Path.Combine(d, "settings.json.old"));
                Check("W19-2 migration (exe-folder layout): every runtime file copied into 「数据 Data」, old one renamed <name>.old; unrelated files, *.tmp/*.bad of others and 准星样式 stay",
                    d == Path.Combine(root, "数据 Data") && allCopied && allRenamed && content && foreignKept && crossKept && note != null && note2 == null && noOldInData,
                    "copied=" + allCopied + " renamed=" + allRenamed + " content=" + content + " foreign=" + foreignKept + " cross=" + crossKept + " note=" + note + " note2=" + note2);
            }

            // W19-3 migration from the parent-folder (bat) layout: <root>\启动 Start-HipAimer.bat + <root>\程序 app\exe
            {
                string root = W19Root();
                string app = Path.Combine(root, "程序 app");
                Directory.CreateDirectory(app);
                File.WriteAllText(Path.Combine(root, "启动 Start-HipAimer.bat"), "@echo off");
                File.WriteAllText(Path.Combine(root, "settings.json"), W19Settings());
                File.WriteAllText(Path.Combine(root, "filter-prefs.json"), "{}");
                Directory.CreateDirectory(Path.Combine(root, "准星样式 Crosshairs"));
                string oldDir = SettingsIO.UserDirFor(app, File.Exists);
                string note;
                string d = SettingsIO.DataDirFor(app, oldDir, out note);
                bool where = oldDir == root && d == Path.Combine(app, "数据 Data");
                bool moved = File.Exists(Path.Combine(d, "settings.json")) && File.Exists(Path.Combine(d, "filter-prefs.json"))
                    && File.Exists(Path.Combine(root, "settings.json.old")) && !File.Exists(Path.Combine(root, "settings.json"));
                bool batKept = File.Exists(Path.Combine(root, "启动 Start-HipAimer.bat")) && Directory.Exists(Path.Combine(root, "准星样式 Crosshairs"));
                // already in 数据 Data: the new copy wins, a stale old file is left alone
                File.WriteAllText(Path.Combine(root, "settings.json"), "stale");
                string before = File.ReadAllText(Path.Combine(d, "settings.json")), note2;
                SettingsIO.DataDirFor(app, oldDir, out note2);
                bool newWins = File.ReadAllText(Path.Combine(d, "settings.json")) == before && File.ReadAllText(Path.Combine(root, "settings.json")) == "stale";
                Check("W19-3 migration (bat layout): files from the folder next to the bat go to 程序 app\\数据 Data; bat + 准星样式 stay; a file already in 数据 Data is never overwritten",
                    where && moved && batKept && newWins, "where=" + where + " moved=" + moved + " bat=" + batKept + " newWins=" + newWins + " note=" + note);
            }

            // W19-4 read-only fallback: 数据 Data cannot be created -> the old location, nothing moved
            {
                string root = W19Root();
                File.WriteAllText(Path.Combine(root, "settings.json"), W19Settings());
                // a FILE named 「数据 Data」 blocks the folder on every OS (chmod is not honoured for root / on Windows)
                File.WriteAllText(Path.Combine(root, "数据 Data"), "x");
                string note;
                string d = SettingsIO.DataDirFor(root, root, out note);
                bool fb = d == root && File.Exists(Path.Combine(root, "settings.json")) && !File.Exists(Path.Combine(root, "settings.json.old")) && note != null;
                // a read-only exe folder (chmod 555) when the OS honours it
                string ro = W19Root();
                File.WriteAllText(Path.Combine(ro, "settings.json"), "{}");
                bool roOk = true; string roInfo = "skipped";
                try
                {
                    if (Environment.OSVersion.Platform == PlatformID.Unix)
                    {
                        File.SetUnixFileMode(ro, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                        bool honoured;
                        try { File.WriteAllText(Path.Combine(ro, "probe"), ""); File.Delete(Path.Combine(ro, "probe")); honoured = false; }
                        catch (Exception) { honoured = true; }
                        if (honoured)
                        {
                            string n2;
                            roOk = SettingsIO.DataDirFor(ro, ro, out n2) == ro && File.Exists(Path.Combine(ro, "settings.json"));
                            roInfo = "ro=" + roOk;
                        }
                        File.SetUnixFileMode(ro, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                }
                catch (Exception ex) { roInfo = "chmod " + ex.GetType().Name; }
                Check("W19-4 data folder cannot be created / written -> old location, nothing renamed", fb && roOk, "fb=" + fb + " " + roInfo + " note=" + note);
            }

            // W19-5 crosshair folder stays at the top level (TopDir), not in 数据 Data; DataFolderName is the agreed name
            {
                string c = SettingsIO.CrosshairDir();
                bool top = c == Path.Combine(SettingsIO.TopDir(), SettingsIO.CrosshairFolderName) && c.IndexOf(SettingsIO.DataFolderName, StringComparison.Ordinal) < 0;
                Check("W19-5 「准星样式 Crosshairs」 stays at the top level (not under 「数据 Data」); folder name 「数据 Data」",
                    top && SettingsIO.DataFolderName == "数据 Data", c);
            }

            // W19-6 import picker: an old folder's settings.json at the top level, in 数据 Data, or in <pkg>\程序 app\数据 Data
            {
                string root = W19Root();
                string top = Path.Combine(root, "old1"); Directory.CreateDirectory(top);
                File.WriteAllText(Path.Combine(top, "settings.json"), W19Settings());
                string inData = Path.Combine(root, "old2"); Directory.CreateDirectory(Path.Combine(inData, "数据 Data"));
                File.WriteAllText(Path.Combine(inData, "数据 Data", "settings.json"), W19Settings());
                File.WriteAllText(Path.Combine(inData, "settings.json.old"), W19Settings());
                string pkgParent = Path.Combine(root, "old3"); string pkgData = Path.Combine(pkgParent, "TarkovHipAimer", "程序 app", "数据 Data");
                Directory.CreateDirectory(pkgData);
                File.WriteAllText(Path.Combine(pkgData, "settings.json"), W19Settings());
                string a = SettingsIO.FindLegacyFile(top), b = SettingsIO.FindLegacyFile(inData), c = SettingsIO.FindLegacyFile(pkgParent);
                bool ok = a == Path.Combine(top, "settings.json") && b == Path.Combine(inData, "数据 Data", "settings.json")
                    && c == Path.Combine(pkgData, "settings.json");
                Check("W19-6 import picker finds settings.json in an old folder (top level), in its 「数据 Data」 and in <unzipped>\\<pkg>\\程序 app\\数据 Data",
                    ok, "a=" + a + " b=" + b + " c=" + c);
            }

            // W19-7 IsRuntimeFile: ours yes, *.old / foreign no
            {
                bool y = SettingsIO.IsRuntimeFile("settings.json") && SettingsIO.IsRuntimeFile("shade-recovery.bad-20261101-1.json")
                    && SettingsIO.IsRuntimeFile("net-prefs.json.tmp") && SettingsIO.IsRuntimeFile("settings.before-import-20261101-101010-2.json");
                bool n = !SettingsIO.IsRuntimeFile("settings.json.old") && !SettingsIO.IsRuntimeFile("settings.json.old-2")
                    && !SettingsIO.IsRuntimeFile("other.tmp") && !SettingsIO.IsRuntimeFile("说明 README.txt") && !SettingsIO.IsRuntimeFile(null);
                Check("W19-7 runtime file names: ours (backups, .bad, .tmp) migrate; *.old and foreign files never", y && n, "y=" + y + " n=" + n);
            }
        }
    }
}
