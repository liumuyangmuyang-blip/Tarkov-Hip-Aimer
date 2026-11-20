// UI-SHELL (v4.0) pure checks: UiPages (tutorial jumpToPage keys), UiScale (dynamic font), Theme.Decode end state,
// KeyCapture (combo capture state machine). Compiled with UI_PURE_CHECK + THEME_PURE_CHECK (no WinForms).
using System;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V4ShellInit() { V4ShellTests(); }

        static void V4ShellTests()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // SH1 every key TutorialForm passes to jumpToPage (TutorialPages in TutorialFramework.cs) opens the right page
            string[] keys = { "首页", "按键", "常规", "后坐力", "移动", "侧身", "换手", "跳跃", "外观", "滤镜", "指南", "反馈" }; // v4.2: 滤镜 after 外观
            string[] want = { "dashboard", "keys", "general", "recoil", "movement", "lean", "hand", "jump", "style", "filter", "guide", "feedback" };
            // v5.1 (W34): 13 pages -- W26's 常见问题 (id "faq", decision 13) appended after 反馈; the first 12 are unchanged
            bool ok1 = UiPages.Ids.Length == 13 && UiPages.Titles.Length == 13 && UiPages.Codes.Length == 13 && UiPages.Aliases.Length == 13
                && UiPages.Ids[12] == "faq" && UiPages.IndexOf("常见问题") == 12 && UiPages.IndexOf("FAQ") == 12;
            string bad1 = "";
            for (int i = 0; i < keys.Length; i++)
            {
                int ix = UiPages.IndexOf(keys[i]);
                if (ix < 0 || UiPages.Ids[ix] != want[i]) { ok1 = false; bad1 += keys[i] + "->" + ix + " "; }
                if (UiPages.IndexOf(want[i]) != i || UiPages.IndexOf(want[i].ToUpperInvariant()) != i) { ok1 = false; bad1 += want[i] + " "; }
                if (UiPages.IndexOf(UiPages.Titles[i]) != i) { ok1 = false; bad1 += UiPages.Titles[i] + " "; }
            }
            foreach (string s in new string[] { null, "", "  ", "nope", "分辨", "|" })
                if (UiPages.IndexOf(s) != -1) { ok1 = false; bad1 += "[" + s + "] "; }
            if (UiPages.IndexOf(" 按键 ") != 1 || UiPages.IndexOf("分辨率") != 2 || UiPages.IndexOf("准星样式") != 8 || UiPages.IndexOf("画面滤镜") != 9 || UiPages.IndexOf("说明") != 10 || UiPages.IndexOf("README") != 10 || UiPages.Ids[11] != "feedback") { ok1 = false; bad1 += "aliases "; }
            Check("SH1 UiPages: tutorial keys + ids + titles + aliases (13 pages, 滤镜 after 外观, 指南 before 反馈, v5.1 常见问题 last)", ok1, bad1);

            // SH7 指南 page: headings / rules found in the real guide text, every line kept, nothing lost
            string[] gl = GuideFormat.Lines(GuideText.Text);
            int[] gk = GuideFormat.Kinds(gl);
            int heads = 0, rules = 0, bracketHeads = 0, bracketAll = 0; bool ok7 = gl.Length == gk.Length && gl.Length > 20; string bad7 = "";
            for (int i = 0; i < gl.Length; i++)
            {
                string tl = gl[i].Trim();
                if (gk[i] == GuideFormat.Heading) heads++;
                if (gk[i] == GuideFormat.Rule) rules++;
                if (tl.StartsWith("【") && tl.EndsWith("】")) { bracketAll++; if (gk[i] == GuideFormat.Heading) bracketHeads++; }
                if (gk[i] == GuideFormat.Heading && tl.Length == 0) { ok7 = false; bad7 += "empty-head@" + i + " "; }
            }
            if (bracketAll == 0 || bracketHeads != bracketAll || rules < 2 || gk[0] != GuideFormat.Heading) { ok7 = false; bad7 += "h" + heads + " r" + rules + " b" + bracketHeads + "/" + bracketAll + " "; }
            if (string.Join("\n", gl) != GuideText.Text.Replace("\r\n", "\n").Replace('\r', '\n')) { ok7 = false; bad7 += "text-lost "; }
            int[] small = GuideFormat.Kinds(new string[] { "Title", "=====", "Sec", "=====", "body", "---", "【X】", "" });
            if (small[0] != 1 || small[1] != 2 || small[2] != 1 || small[4] != 0 || small[5] != 0 || small[6] != 1 || small[7] != 0) { ok7 = false; bad7 += "small "; }
            if (GuideFormat.IsRule("====") || !GuideFormat.IsRule(" ------ ") || GuideFormat.IsRule("==a==") || GuideFormat.Kinds(new string[0]).Length != 0) { ok7 = false; bad7 += "rule "; }
            string rtf = GuideFormat.Rtf(gl, gk, "Microsoft YaHei UI", 11f, System.Drawing.Color.White, System.Drawing.Color.Lime, System.Drawing.Color.Gray);
            int depth = 0, minDepth = 0, bolds = 0; for (int i = 0; i < rtf.Length; i++) { if (rtf[i] == '\\') { i++; continue; } if (rtf[i] == '{') depth++; if (rtf[i] == '}') { depth--; if (i < rtf.Length - 1) minDepth = Math.Min(minDepth, depth); } }
            for (int ix = rtf.IndexOf("\\cf2\\b "); ix >= 0; ix = rtf.IndexOf("\\cf2\\b ", ix + 1)) bolds++;
            if (depth != 0 || minDepth < 0 || bolds != heads || !rtf.StartsWith("{\\rtf1") || !rtf.Contains("\\fs22 ") || rtf.Contains("【")) { ok7 = false; bad7 += "rtf d" + depth + " b" + bolds + " "; }
            Check("SH7 指南 GuideFormat on GuideText.Text: " + heads + " headings, " + rules + " rules, " + gl.Length + " lines", ok7, bad7);

            // SH4 Theme.Decode: the finished text is the real text (page code tags never end on a scrambled char)
            bool ok4 = true; string bad4 = "";
            foreach (string c in UiPages.Codes)
            {
                if (Theme.Decode(c, 1.0, 3) != c || Theme.Decode(c, 2.0, 9) != c || Theme.Decode(c, double.NaN, 1) != c) { ok4 = false; bad4 += c + " "; }
                if (Theme.Decode(c, 0.0, 3).Length != c.Length) { ok4 = false; bad4 += "len:" + c + " "; }
                // PageTitle: sub decodes at p*1.3-0.2 -> done before the 420 ms title animation ends
                if (Theme.Decode(c, 1.0 * 1.3 - 0.2, 4) != c) { ok4 = false; bad4 += "end:" + c + " "; }
            }
            foreach (string t in UiPages.Titles)
                if (Theme.Decode(t, 1.0, 5) != t) { ok4 = false; bad4 += t + " "; }
            if (Theme.Decode(null, 0.5, 1) != "" || Theme.Decode("", 0.5, 1) != "") { ok4 = false; bad4 += "null "; }
            Check("SH4 Theme.Decode final text == real text", ok4, bad4);

            // SH5 KeyCapture: Ctrl then D = Ctrl+D, Shift alone = Shift, Esc cancels, mouse with Alt
            bool ok5 = true; string bad5 = "";
            KeyCapture kc = new KeyCapture();
            kc.Start();
            if (kc.KeyDown(0xA2, KeyBindings.ModCtrl) != KeyCapture.Result.Waiting || kc.Shown != "Ctrl+…") { ok5 = false; bad5 += "ctrl-wait "; }
            if (kc.KeyDown(0x44, KeyBindings.ModCtrl) != KeyCapture.Result.Done || kc.Vk != 0x44 || kc.Mods != KeyBindings.ModCtrl) { ok5 = false; bad5 += "ctrl+d "; }
            kc.Start();
            kc.KeyDown(0x10, KeyBindings.ModShift);
            if (kc.KeyUp(0x10, 0) != KeyCapture.Result.Done || kc.Vk != 0x10 || kc.Mods != 0) { ok5 = false; bad5 += "shift "; }
            kc.Start();
            if (kc.KeyDown(KeyCapture.VkEscape, 0) != KeyCapture.Result.Cancelled) { ok5 = false; bad5 += "esc "; }
            kc.Start();
            if (kc.Mouse(5, KeyBindings.ModAlt) != KeyCapture.Result.Done || kc.Vk != 5 || kc.Mods != KeyBindings.ModAlt) { ok5 = false; bad5 += "mouse "; }
            kc.Start();
            kc.KeyDown(0xA2, KeyBindings.ModCtrl);
            kc.KeyDown(0xA0, KeyBindings.ModCtrl | KeyBindings.ModShift); // two modifiers: releasing one must not bind it
            if (kc.KeyUp(0xA0, KeyBindings.ModCtrl) != KeyCapture.Result.Waiting) { ok5 = false; bad5 += "two-mods "; }
            Check("SH5 KeyCapture combos / modifier alone / Esc / mouse", ok5, bad5);

            // SH6 key conflict: 是 MOVES the key (other action gets the old key); W/A/S/D never change; brute force
            bool ok6 = true; string bad6 = "";
            KeyBindings kb = new KeyBindings();
            kb.SetDefaults();
            int ll = KeyBindings.ActionLeanLeft, lr = KeyBindings.ActionLeanRight;
            int qVk = kb.Get(ll), eVk = kb.Get(lr);
            int oth = KeyConflict.Find(kb, qVk, 0, lr);
            string q6 = KeyConflict.Question(kb, lr, qVk, 0, oth);
            KeyConflict.Apply(kb, lr, qVk, 0, oth);
            if (oth != ll || kb.Get(lr) != qVk || kb.Get(ll) != eVk || !q6.Contains("换过来")) { ok6 = false; bad6 += "swapQE(" + oth + "," + q6 + ") "; }
            int dVk = 0x44;
            int othD = KeyConflict.Find(kb, dVk, 0, KeyBindings.ActionJump);
            string qd = KeyConflict.Question(kb, KeyBindings.ActionJump, dVk, 0, othD);
            KeyConflict.Apply(kb, KeyBindings.ActionJump, dVk, 0, othD);
            if (othD != KeyBindings.ActionRight || kb.Get(KeyBindings.ActionJump) != dVk || kb.Get(KeyBindings.ActionRight) != dVk || !qd.Contains("固定"))
            { ok6 = false; bad6 += "movement(" + othD + "," + qd + ") "; }
            // brute force: random captures answered 是; non-movement actions never share a combo, W/A/S/D stay fixed
            Random rnd = new Random(4242);
            int[] pool = { 0x51, 0x45, 0x44, 0x41, 0x57, 0x53, 0x20, 0x10, 0x11, 0x12, 0x01, 0x02, 0x04, 0x05, 0x06, 0x7B, 0x43, 0x58, 0x5A, 0x56 };
            int brute = 0;
            for (int round = 0; round < 200 && ok6; round++)
            {
                KeyBindings b = new KeyBindings();
                b.SetDefaults();
                // make the start unique among non-movement actions (Apply keeps an already unique set unique)
                for (int a = 0; a < KeyBindings.ActionCount; a++)
                {
                    if (KeyBindings.IsMovementAction(a)) continue;
                    for (int c = 0; c < a; c++)
                        if (!KeyBindings.IsMovementAction(c) && b.Get(c) == b.Get(a) && b.GetMods(c) == b.GetMods(a)) b.SetCombo(a, 0x70 + a, 0);
                }
                for (int step = 0; step < 100; step++)
                {
                    int act; do { act = rnd.Next(KeyBindings.ActionCount); } while (KeyBindings.IsMovementAction(act));
                    int vk = pool[rnd.Next(pool.Length)], mods = rnd.Next(8);
                    int o = KeyConflict.Find(b, vk, mods, act);
                    KeyConflict.Question(b, act, vk, mods, o >= 0 ? o : 0);
                    KeyConflict.Apply(b, act, vk, mods, o);
                    brute++;
                    if (b.Get(KeyBindings.ActionForward) != 0x57 || b.Get(KeyBindings.ActionBack) != 0x53 || b.Get(KeyBindings.ActionLeft) != 0x41 || b.Get(KeyBindings.ActionRight) != 0x44)
                    { ok6 = false; bad6 += "wasd r" + round + " "; break; }
                    for (int a = 0; a < KeyBindings.ActionCount && ok6; a++)
                        for (int c = 0; c < a; c++)
                            if (!KeyBindings.IsMovementAction(a) && !KeyBindings.IsMovementAction(c) && b.Get(a) == b.Get(c) && b.GetMods(a) == b.GetMods(c))
                            { ok6 = false; bad6 += "dup " + KeyBindings.ActionNames[a] + "/" + KeyBindings.ActionNames[c] + " r" + round + "s" + step + " "; break; }
                    if (!ok6) break;
                }
            }
            Check("SH6 key conflict 是 = swap (" + brute + " brute captures), W/A/S/D fixed", ok6, bad6);
        }

        static bool Near(double got, double want, ref string bad, string what)
        {
            if (Math.Abs(got - want) < 1e-9) return true;
            bad += what + "=" + got + "(want " + want + ") ";
            return false;
        }
    }
}
