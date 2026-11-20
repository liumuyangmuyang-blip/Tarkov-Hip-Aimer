using System;
using System.Collections.Generic;
using System.IO;

namespace TarkovCrosshair.Tests
{
    // v3.2: 点一下切换开镜 fixes — Tab/Esc clears the latch, Alt/Ctrl+右键 is not 开镜, F12 off->on clears the latch.
    public static partial class Program
    {
        sealed class KSim
        {
            public MotionEngine E = new MotionEngine();
            public Profile P; public GlobalSettings G; public double Fps = 144;
            public HashSet<int> Down = new HashSet<int>();
            public KSim(GlobalSettings g) { G = g; P = g.Profiles[0]; }
            public void Step()
            {
                InputState i = MotionEngine.ReadInput(G.Keys, delegate(int vk) { return Down.Contains(vk); });
                E.Update(1.0 / Fps, i, P, G, PreviewMode.Live, 1.0);
            }
            public void Run(double s) { int n = (int)Math.Round(s * Fps); for (int k = 0; k < n; k++) Step(); }
            public void Press(int vk) { Down.Add(vk); Run(0.08); }
            public void Release(int vk) { Down.Remove(vk); Run(0.08); }
            public void Tap(int vk) { Press(vk); Release(vk); }
            public bool Shown { get { Run(0.2); return E.Alpha > 0.99; } }
            public bool Hidden { get { Run(0.2); return E.Alpha < 0.01; } }
        }

        static GlobalSettings G32(string adsMode)
        {
            GlobalSettings g = GlobalSettings.CreateDefault();
            g.AdsMode = adsMode; g.OnlyWhenGameFocused = false;
            return g;
        }

        const int RB = 0x02, TAB = 0x09, ESC = 0x1B, ALT = 0x12, CTRL = 0x11, F12 = 0x7B;

        static void V32Tests()
        {
            // 1. Tab / Esc clear the toggle latch (the inventory bug)
            foreach (int cancel in new int[] { TAB, ESC })
            {
                KSim s = new KSim(G32("toggle"));
                s.Run(0.3);
                s.Tap(RB); bool h1 = s.Hidden;
                s.Tap(cancel); bool shownAfter = s.Shown && !s.E.AdsToggled;
                s.Tap(RB); bool h2 = s.Hidden;
                s.Tap(RB); bool s2 = s.Shown;
                Check("V32-1 toggle: " + (cancel == TAB ? "Tab" : "Esc") + " clears 开镜", h1 && shownAfter && h2 && s2,
                    "hidden=" + h1 + " shownAfterCancel=" + shownAfter + " nextTapHides=" + h2 + " thenShows=" + s2);
                // cancel while not aimed: no effect
                s.Tap(cancel); bool still = s.Shown; s.Tap(RB); bool h3 = s.Hidden;
                Check("V32-1b cancel when not aimed is harmless (" + cancel + ")", still && h3, "shown=" + still + " tapHides=" + h3);
            }

            // 2. Alt/Ctrl + 右键 is not 开镜 (toggle)
            foreach (int mod in new int[] { ALT, CTRL })
            {
                KSim s = new KSim(G32("toggle"));
                s.Run(0.3);
                s.Press(mod); s.Tap(RB); s.Release(mod);
                bool notAimed = s.Shown && !s.E.AdsToggled;
                // release order: mod first, then RB
                s.Press(mod); s.Press(RB); s.Release(mod); s.Release(RB);
                bool notAimed2 = s.Shown && !s.E.AdsToggled;
                // while aimed, Alt+RB (切倍率) keeps 开镜
                s.Tap(RB); bool aimed = s.Hidden;
                s.Press(mod); s.Tap(RB); s.Tap(RB); s.Release(mod);
                bool stillAimed = s.Hidden && s.E.AdsToggled;
                s.Tap(RB); bool out1 = s.Shown;
                Check("V32-2 toggle: " + (mod == ALT ? "Alt" : "Ctrl") + "+右键 ignored", notAimed && notAimed2 && aimed && stillAimed && out1,
                    "notAimed=" + notAimed + " modFirstRelease=" + notAimed2 + " aimed=" + aimed + " zoomKeepsAim=" + stillAimed + " exit=" + out1);
            }

            // 3. Hold mode: Alt+右键 does not hide; plain 右键 still hides; Tab does nothing in hold mode
            {
                KSim s = new KSim(G32("hold"));
                s.Run(0.3);
                s.Press(ALT); s.Press(RB); bool shown = s.Shown; s.Release(RB); s.Release(ALT);
                s.Press(RB); bool hid = s.Hidden; s.Press(ALT); bool hid2 = s.Hidden; s.Release(ALT); s.Release(RB);
                bool back = s.Shown;
                s.Press(RB); s.Tap(TAB); bool holdTab = s.Hidden; s.Release(RB);
                Check("V32-3 hold: Alt+右键 shown, 右键 hides, Alt mid-aim keeps hidden, Tab no effect", shown && hid && hid2 && back && holdTab,
                    shown + "/" + hid + "/" + hid2 + "/" + back + "/" + holdTab);
            }

            // 4. Both options off = v3.1 behaviour
            {
                GlobalSettings g = G32("toggle"); g.AdsCancelTabEsc = false; g.AdsIgnoreAltCtrl = false;
                KSim s = new KSim(g);
                s.Run(0.3);
                s.Tap(RB); s.Tap(TAB); bool stays = s.Hidden;
                s.Tap(RB); s.Press(ALT); s.Tap(RB); s.Release(ALT); bool altToggles = s.Hidden;
                Check("V32-4 options off = old behaviour", stays && altToggles, "TabKeepsAim=" + stays + " AltRBToggles=" + altToggles);
            }

            // 5. F12 off -> on clears the latch
            {
                GlobalSettings g = G32("toggle");
                KSim s = new KSim(g);
                s.Run(0.3);
                s.Tap(RB); bool h = s.Hidden;
                g.Enabled = false; s.Run(0.2); g.Enabled = true; s.Run(0.05);
                bool cleared = !s.E.AdsToggled && s.Shown;
                s.Tap(RB); bool h2 = s.Hidden;
                Check("V32-5 F12 twice clears 开镜 latch", h && cleared && h2, h + "/" + cleared + "/" + h2);
            }

            // 6. Custom 开镜 key (not 右键): Alt filter off for it
            {
                GlobalSettings g = G32("toggle"); g.Keys.Ads = 0x46; // F
                KSim s = new KSim(g);
                s.Run(0.3);
                s.Press(ALT); s.Tap(0x46); s.Release(ALT);
                Check("V32-6 custom 开镜 key ignores the Alt filter", s.Hidden && s.E.AdsToggled, "toggled=" + s.E.AdsToggled);
            }

            // 7. Fuzz: random RB/Tab/Esc/Alt/Ctrl/move/fire sequences; toggle latch must equal a reference model
            {
                Random r = new Random(32);
                int bad = 0, runs = 2000;
                for (int it = 0; it < runs; it++)
                {
                    KSim s = new KSim(G32("toggle"));
                    s.Fps = new double[] { 60, 144, 240, 360 }[it % 4];
                    bool model = false, blocked = false, prevRb = false, prevTab = false, prevEsc = false;
                    int[] keys = { RB, TAB, ESC, ALT, CTRL, 0x41, 0x44, 0x57, 0x01, 0x10 };
                    bool prevSpr = false;
                    for (int step = 0; step < 60; step++)
                    {
                        int vk = keys[r.Next(keys.Length)];
                        if (s.Down.Contains(vk)) s.Down.Remove(vk); else s.Down.Add(vk);
                        int frames = 1 + r.Next(12);
                        for (int f = 0; f < frames; f++)
                        {
                            s.Step();
                            bool rb = s.Down.Contains(RB);
                            bool mod = s.Down.Contains(ALT) || s.Down.Contains(CTRL);
                            // v4.1 (BRUTE40 P2-3, MOTION41F): Tab and Esc each cancel on their own press, also while the other
                            // one is held (was one merged Tab||Esc level, so Esc while Tab was held did not cancel).
                            bool tab = s.Down.Contains(TAB), esc = s.Down.Contains(ESC);
                            if (rb && !prevRb) blocked = mod;
                            if (!rb) blocked = false;
                            if (rb && !prevRb && !blocked) model = !model;
                            if ((tab && !prevTab) || (esc && !prevEsc)) model = false;
                            bool spr = s.Down.Contains(0x10) && s.Down.Contains(0x57);
                            if (spr && !prevSpr) model = false; prevSpr = spr;
                            prevRb = rb; prevTab = tab; prevEsc = esc;
                            if (s.E.AdsToggled != model) bad++;
                        }
                    }
                }
                Check("V32-7 fuzz 2000 runs: latch matches model", bad == 0, "mismatched frames=" + bad);
            }

            // 9. v3.3 冲刺取消开镜: Shift+W either order clears; Shift alone (屏息) keeps; W alone keeps; option off keeps
            {
                const int SH = 0x10, W = 0x57;
                KSim s = new KSim(G32("toggle")); s.Run(0.3);
                s.Tap(RB); s.Press(SH); bool breath = s.Hidden && s.E.AdsToggled; s.Release(SH);
                s.Press(W); bool walk = s.Hidden; s.Press(SH); bool a = s.Shown && !s.E.AdsToggled; s.Release(SH); s.Release(W);
                s.Tap(RB); s.Press(SH); s.Press(W); bool b = s.Shown; s.Release(W); s.Release(SH);
                s.Tap(RB); bool reaim = s.Hidden;
                GlobalSettings g = G32("toggle"); g.AdsCancelSprint = false; KSim o = new KSim(g); o.Run(0.3);
                o.Tap(RB); o.Press(W); o.Press(SH); bool off = o.Hidden;
                KSim h = new KSim(G32("hold")); h.Run(0.3); h.Press(RB); h.Press(W); h.Press(SH); bool hold = h.Hidden;
                Check("V33-1 Shift+W cancels toggle 开镜, Shift alone does not", breath && walk && a && b && reaim && off && hold,
                    "breath=" + breath + " walk=" + walk + " W>Shift=" + a + " Shift>W=" + b + " reaim=" + reaim + " optOff=" + off + " holdMode=" + hold);
            }

            // 8. Settings: old file without the fields -> both on; saved values round-trip; 按键恢复默认 restores them
            {
                string dir = Path.Combine(Path.GetTempPath(), "v32-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string p1 = Path.Combine(dir, "old.json");
                File.WriteAllText(p1, "{\"AdsMode\":\"toggle\"}");
                string msg;
                GlobalSettings a = SettingsIO.Load(p1, out msg);
                bool oldOn = a.AdsCancelTabEsc && a.AdsIgnoreAltCtrl && a.AdsCancelSprint;
                a.AdsCancelTabEsc = false; a.AdsIgnoreAltCtrl = false; a.AdsCancelSprint = false;
                string p2 = Path.Combine(dir, "new.json");
                File.WriteAllText(p2, SettingsIO.Serialize(a));
                GlobalSettings b = SettingsIO.Load(p2, out msg);
                bool rt = !b.AdsCancelTabEsc && !b.AdsIgnoreAltCtrl && !b.AdsCancelSprint;
                b.ResetKeys();
                bool reset = b.AdsCancelTabEsc && b.AdsIgnoreAltCtrl && b.AdsCancelSprint;
                GlobalSettings d = GlobalSettings.CreateDefault();
                bool def = d.AdsCancelTabEsc && d.AdsIgnoreAltCtrl && d.AdsMode == "hold";
                File.Delete(p1); File.Delete(p2); Directory.Delete(dir);
                Check("V32-8 settings migrate/round-trip/reset", oldOn && rt && reset && def, oldOn + "/" + rt + "/" + reset + "/" + def);
            }
        }
    }
}
