// v5.1.1 (X16, Leo 21 Nov 2026): 跳跃 触发方式 (高级设置). JumpSettings.Trigger "press" = 按下时触发 (default, old files,
// bit-identical: TestsV511X16Gold.cs) or "release" = 松开时触发 (the jump path starts on the frame the jump key is released).
using System;
using System.Collections.Generic;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X16Init() { V511X16Tests(); }

        static Profile X16P(string trigger)
        {
            Profile p = X18Pre("x16"); p.Jump.Enabled = true; p.Jump.Trigger = trigger;
            return p;
        }

        /// <summary>Runs frames at 144 fps; keyAt(frame) gives the input; returns per-frame Jumping and JumpMs.</summary>
        static void X16Run(Profile p, GlobalSettings g, int frames, Func<int, InputState> keyAt, Func<int, bool> focusAt,
            List<bool> jumping, List<double> ms)
        {
            MotionEngine e = new MotionEngine(); e.GameFocused = true; e.OwnForeground = true;
            for (int k = 0; k < frames; k++)
            {
                if (focusAt != null) e.GameFocused = focusAt(k);
                e.Update(1.0 / 144, keyAt(k), p, g, PreviewMode.Live, 1.0);
                jumping.Add(e.Jumping); ms.Add(e.JumpMs);
            }
        }

        static InputState X16Key(bool jump, bool ads)
        {
            InputState i = new InputState(); i.Jump = jump; i.Ads = ads; return i;
        }

        static int X16Starts(List<bool> j)
        {
            int n = 0;
            for (int k = 0; k < j.Count; k++) if (j[k] && (k == 0 || !j[k - 1])) n++;
            return n;
        }

        static int X16First(List<bool> j)
        {
            for (int k = 0; k < j.Count; k++) if (j[k]) return k;
            return -1;
        }

        static void V511X16Tests()
        {
            GlobalSettings g = new GlobalSettings(); g.OnlyWhenGameFocused = false;
            double dtMs = 1000.0 / 144;

            // X16-2 release mode: press at frame 20, hold 72 frames (0.5 s), release at frame 92: nothing while held, the path
            // starts ON the release frame (JumpMs = one frame); press mode with the same keys starts on the press frame
            {
                List<bool> jr = new List<bool>(), jp = new List<bool>(); List<double> mr = new List<double>(), mp = new List<double>();
                Func<int, InputState> keys = delegate(int k) { return X16Key(k >= 20 && k < 92, false); };
                X16Run(X16P(JumpSettings.TriggerRelease), g, 300, keys, null, jr, mr);
                X16Run(X16P(JumpSettings.TriggerPress), g, 300, keys, null, jp, mp);
                int fr = X16First(jr), fp = X16First(jp);
                bool ok = fr == 92 && Math.Abs(mr[92] - dtMs) < 1e-9 && fp == 20 && Math.Abs(mp[20] - dtMs) < 1e-9 && X16Starts(jr) == 1 && X16Starts(jp) == 1;
                Check("X16-2 松开时触发: no jump while the key is held, the path starts on the release frame (92), 按下时触发 on the press frame (20)", ok,
                    "release first=" + fr + " press first=" + fp);
            }
            // X16-3 hold 2 s then release = exactly one jump (release mode); holding 2 s in press mode = one jump too (no retrigger)
            {
                List<bool> jr = new List<bool>(), jp = new List<bool>(); List<double> mr = new List<double>(), mp = new List<double>();
                Func<int, InputState> keys = delegate(int k) { return X16Key(k >= 10 && k < 10 + 288, false); }; // 288 frames = 2 s
                X16Run(X16P(JumpSettings.TriggerRelease), g, 900, keys, null, jr, mr);
                X16Run(X16P(JumpSettings.TriggerPress), g, 900, keys, null, jp, mp);
                bool ok = X16Starts(jr) == 1 && X16First(jr) == 298 && X16Starts(jp) == 1 && X16First(jp) == 10;
                Check("X16-3 hold 2 s then release: 松开时触发 = one jump (at the release), 按下时触发 = one jump (at the press)", ok,
                    "release starts=" + X16Starts(jr) + " first=" + X16First(jr) + " press starts=" + X16Starts(jp));
            }
            // X16-4 release while 开镜 (ADS held, ring hidden) still starts the jump, like a press does today; a release during a
            // running jump is ignored (no double jump)
            {
                List<bool> ja = new List<bool>(), jd = new List<bool>(); List<double> ma = new List<double>(), md = new List<double>();
                X16Run(X16P(JumpSettings.TriggerRelease), g, 200, delegate(int k) { return X16Key(k >= 5 && k < 40, true); }, null, ja, ma);
                // two taps 20 frames apart (140 ms < 800 ms jump): one jump
                X16Run(X16P(JumpSettings.TriggerRelease), g, 400, delegate(int k) { return X16Key((k >= 5 && k < 10) || (k >= 30 && k < 35), false); }, null, jd, md);
                bool ok = X16First(ja) == 40 && X16Starts(jd) == 1 && X16First(jd) == 10;
                Check("X16-4 松开时触发 while 开镜 (hidden) counts; a second release during the jump is ignored", ok,
                    "ads first=" + X16First(ja) + " double starts=" + X16Starts(jd));
            }
            // X16-5 只在塔科夫前台时显示 on: the game losing focus while the key is held is not a release (no jump); the
            // real release after focus comes back with the key still up is nothing either
            {
                GlobalSettings gf = new GlobalSettings(); gf.OnlyWhenGameFocused = true;
                List<bool> j = new List<bool>(); List<double> m = new List<double>();
                X16Run(X16P(JumpSettings.TriggerRelease), gf, 300, delegate(int k) { return X16Key(k >= 5 && k < 200, false); },
                    delegate(int k) { return k < 50 || k >= 100; }, j, m);
                // focus lost at 50 (key forced up: no jump), back at 100 with the key held (not a press), released at 200 = one jump
                bool ok = X16Starts(j) == 1 && X16First(j) == 200;
                Check("X16-5 松开时触发: losing game focus while holding is not a release; the real release later starts one jump", ok,
                    "starts=" + X16Starts(j) + " first=" + X16First(j));
            }
            // X16-6 preview 跳跃（站着）: the synthetic 100 ms press (500..600 ms) starts the path at 500 ms (press) or 600 ms (release)
            {
                int[] first = new int[2];
                string[] tr = new string[] { JumpSettings.TriggerPress, JumpSettings.TriggerRelease };
                for (int i = 0; i < 2; i++)
                {
                    MotionEngine e = new MotionEngine(); e.GameFocused = false; e.OwnForeground = true;
                    Profile p = X16P(tr[i]); first[i] = -1;
                    for (int k = 0; k < 400 && first[i] < 0; k++)
                    {
                        e.Update(1.0 / 200, new InputState(), p, g, PreviewMode.JumpStand, 1.0); // 5 ms frames
                        if (e.Jumping) first[i] = k;
                    }
                }
                // previewMs after frame k = 5 (k + 1); Synthetic reads previewMs before the frame's add: press seen at frame 100, release at 120
                bool ok = first[0] >= 99 && first[0] <= 101 && first[1] >= 119 && first[1] <= 121;
                Check("X16-6 preview: 按下时触发 starts at ~500 ms, 松开时触发 at ~600 ms (the synthetic release)", ok, "frames " + first[0] + " / " + first[1]);
            }
            // X16-7 save / load round trip; old files, unknown values, Leo默认 and a blank profile = 按下时触发
            {
                GlobalSettings s = GlobalSettings.CreateDefault();
                Profile p = s.FindProfile(s.ActiveProfile);
                p.Jump.Trigger = JumpSettings.TriggerRelease;
                string js = SettingsIO.Serialize(s);
                GlobalSettings back; string err;
                bool ok = SettingsIO.TryParse(js, out back, out err);
                Profile q = ok ? back.FindProfile(s.ActiveProfile) : null;
                ok = ok && q != null && q.Jump.Trigger == JumpSettings.TriggerRelease && q.Jump.OnRelease && SettingsIO.Serialize(back) == js
                    && js.Contains("\"Trigger\"") && p.Clone().Jump.Trigger == JumpSettings.TriggerRelease;
                Check("X16-7 触发方式 save / load round trip (松开时触发 kept, clone kept)", ok, err ?? "");

                GlobalSettings old, odd; string e1, e2;
                bool ok1 = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"a\",\"Jump\":{\"Enabled\":true,\"TotalMs\":700}}]}", out old, out e1);
                bool ok2 = SettingsIO.TryParse("{\"Profiles\":[{\"Name\":\"a\",\"Jump\":{\"Enabled\":true,\"Trigger\":\"sideways\"}}]}", out odd, out e2);
                Profile o = ok1 ? old.FindProfile("a") : null, u = ok2 ? odd.FindProfile("a") : null;
                bool okDef = o != null && o.Jump.Trigger == JumpSettings.TriggerPress && u != null && u.Jump.Trigger == JumpSettings.TriggerPress
                    && X18Pre("z").Jump.Trigger == JumpSettings.TriggerPress && Profile.CreateBlank("z").Jump.Trigger == JumpSettings.TriggerPress
                    && new JumpSettings().Trigger == JumpSettings.TriggerPress;
                Check("X16-8 old file / unknown value / Leo默认 / blank / new: 按下时触发", ok1 && ok2 && okDef, (e1 ?? "") + (e2 ?? ""));
            }
            // X16-9 help entry
            {
                bool ok = HelpText.Body("jumptrigger", g).Contains("松开时触发") && HelpText.Body("jumptrigger", g).Contains("按下时触发");
                Check("X16-9 「?」 help entry jumptrigger explains both 触发方式", ok, "");
            }
        }
    }
}
