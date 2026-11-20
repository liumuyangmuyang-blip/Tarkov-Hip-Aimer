// UiPrefs.cs -- the 「高级设置」 switch (v4.0, UI-POLISH) -- by Leo.
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace TarkovCrosshair
{
    /// <summary>
    /// 「高级设置」 (default OFF): when off, every settings page shows only what a normal player needs.
    /// Stored in GlobalSettings.AdvancedUi (bool, [DataMember "AdvancedUi"]) when that field exists (requested from CORE in
    /// v4-requests/UI.md); until then in a one-line file 「ui-advanced.txt」 next to settings.json.
    /// </summary>
    internal static class UiPrefs
    {
        public const string FieldName = "AdvancedUi";
        public const string FileName = "ui-advanced.txt";
        private static readonly FieldInfo field = typeof(GlobalSettings).GetField(FieldName, BindingFlags.Public | BindingFlags.Instance);
        private static bool loaded, fileValue;

        /// <summary>True when GlobalSettings carries the switch itself (saved in settings.json).</summary>
        public static bool InSettings { get { return field != null && field.FieldType == typeof(bool); } }

        public static bool Advanced(GlobalSettings g)
        {
            if (InSettings && g != null) { try { return (bool)field.GetValue(g); } catch (Exception) { } }
            if (!loaded)
            {
                loaded = true;
                try
                {
                    string p = FilePath();
                    fileValue = p != null && File.Exists(p) && File.ReadAllText(p, Encoding.UTF8).Trim() == "1";
                }
                catch (Exception) { fileValue = false; }
            }
            return fileValue;
        }

        public static void SetAdvanced(GlobalSettings g, bool on)
        {
            if (InSettings && g != null) { try { field.SetValue(g, on); return; } catch (Exception) { } }
            loaded = true;
            fileValue = on;
            // v5.0.2 (decision 5: no file work on the UI thread): the one-line file is written on a pool thread; the
            // newest value wins (a quick on / off writes the last state)
            lock (writeLock) { pendingValue = on ? 1 : 0; if (writing) return; writing = true; }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { WriteLoop(); });
        }

        private static readonly object writeLock = new object();
        private static int pendingValue = -1;
        private static bool writing;

        private static void WriteLoop()
        {
            while (true)
            {
                int v;
                lock (writeLock)
                {
                    v = pendingValue;
                    pendingValue = -1;
                    if (v < 0) { writing = false; return; }
                }
                try
                {
                    string p = FilePath();
                    if (p != null) File.WriteAllText(p, v == 1 ? "1" : "0", new UTF8Encoding(false));
                }
                catch (Exception) { } // a read-only folder only loses the switch, never the settings
            }
        }

        /// <summary>Tests: true once every queued write is done.</summary>
        internal static bool WritesIdle { get { lock (writeLock) return !writing; } }

        private static string FilePath()
        {
            try { return Path.Combine(SettingsIO.UserDir(), FileName); }
            catch (Exception) { return null; }
        }
    }
}
