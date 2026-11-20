// v5.1.1 (X1): F1 wizard does not ask 画面有没有被拉宽 (default 拉伸, 有黑边 only in 常规 高级设置); F2 clearly drawn check boxes /
// radio buttons + really exclusive radio groups; F3 wizard skips 开火 / 开镜 / 跳跃, mouse middle / side buttons bind in every
// key box (MouseEdge poll while waiting), 强化方式 shows both 切换 and 按住. Pure parts + source checks.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TarkovCrosshair.Tests
{
    static partial class Program
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void V511X1Init() { V511X1Tests(); }

        static string X1Src(string file)
        {
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && d != null; i++)
            {
                string c = Path.Combine(d, "tarkov-crosshair-v4", "src");
                if (Directory.Exists(c)) return File.ReadAllText(Path.Combine(c, file));
                d = Path.GetDirectoryName(d.TrimEnd('/', '\\'));
            }
            return File.ReadAllText(Path.Combine("/home/dev/tarkov-crosshair-v4/src", file));
        }

        static string X1Between(string s, string from, string to)
        {
            int a = s.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return "";
            int b = s.IndexOf(to, a + from.Length, StringComparison.Ordinal);
            return b < 0 ? s.Substring(a) : s.Substring(a, b - a);
        }

        /// <summary>A current-format file of g, optionally without one key (= what an older version wrote).</summary>
        static string X1File(GlobalSettings g, string dropKey)
        {
            string j = SettingsIO.Serialize(g);
            if (dropKey != null) j = Regex.Replace(j, "\\s*\"" + dropKey + "\"\\s*:\\s*(\"[^\"]*\"|[^,}\\]]+),?", "");
            return j;
        }

        static void V511X1Tests()
        {
            // X1-1 MouseEdge: only a NEW press of middle / side 1 / side 2 counts; a button already held at the start never does
            {
                HashSet<int> held = new HashSet<int>();
                Func<int, bool> down = delegate(int vk) { return held.Contains(vk); };
                MouseEdge m = new MouseEdge();
                held.Add(0x05);
                m.Reset(down);
                bool heldIgnored = m.NewPress(down) == 0;
                held.Clear();
                bool releaseIgnored = m.NewPress(down) == 0;
                held.Add(0x05);
                bool side1 = m.NewPress(down) == 0x05;
                bool stillHeld = m.NewPress(down) == 0;
                held.Add(0x06);
                bool side2 = m.NewPress(down) == 0x06;
                held.Clear(); m.NewPress(down);
                held.Add(0x04);
                bool middle = m.NewPress(down) == 0x04;
                held.Clear(); m.NewPress(down);
                held.Add(0x01); held.Add(0x02);
                bool lrIgnored = m.NewPress(down) == 0; // left / right stay with the key box's own MouseDown
                Func<int, bool> boom = delegate(int vk) { throw new InvalidOperationException(); };
                bool safe = m.NewPress(boom) == 0 && m.NewPress(null) == 0;
                Check("X1-1 key box mouse poll: new press of middle / side 1 / side 2 reported once, held-at-start / left / right ignored, read errors safe",
                    heldIgnored && releaseIgnored && side1 && stillHeld && side2 && middle && lrIgnored && safe,
                    "held=" + heldIgnored + " rel=" + releaseIgnored + " x1=" + side1 + " still=" + stillHeld + " x2=" + side2 + " mid=" + middle + " lr=" + lrIgnored + " safe=" + safe);
            }

            // X1-2 every key box polls the mouse while it waits and stops when done / cancelled (wizard, 按键 page, 丢雷 / 换弹 boxes)
            {
                string wz = X1Src("WizardForm.cs"), sf = X1Src("SettingsForm.cs"), pk = X1Src("SettingsForm.PageKeys.cs"), km = X1Src("KeyBoxMouse.cs");
                string d = "";
                string wire = X1Between(wz, "private void WireCapture(", "private static int ModsOf(");
                if (!wire.Contains("mousePoll.Start(")) d += " wizard-start";
                if (!X1Between(wz, "private void CancelCapture()", "private void FinishCapture()").Contains("mousePoll.Stop();")) d += " wizard-cancel";
                if (!X1Between(wz, "private void FinishCapture()", "private const int WmKeyUp").Contains("mousePoll.Stop();")) d += " wizard-finish";
                if (!X1Between(sf, "private KeyButton KeyBtn(int action)", "private string KeyText(int action)").Contains("mousePoll.Start(")) d += " page-start";
                if (!X1Between(sf, "private void CancelCapture()", "private void FinishCapture()").Contains("mousePoll.Stop();")) d += " page-cancel";
                if (!X1Between(sf, "private void FinishCapture()", "private bool TryCaptureKey(").Contains("mousePoll.Stop();")) d += " page-finish";
                string vk = X1Between(pk, "private Button VkBtn(", "private NumericUpDown ReloadSecBox(");
                if (!vk.Contains("mousePoll.Start(") || !vk.Contains("mousePoll.Stop();") || !vk.Contains("MouseButtons.Middle")) d += " vkbtn";
                if (!km.Contains("timer.Stop()") || !km.Contains("edge.Reset(Native.IsDown)") || !km.Contains("IntervalMs = 15")) d += " poll";
                Check("X1-2 mouse side / middle buttons bind in every key box: polled only while waiting (wizard, 按键, 丢雷 / 换弹), stopped on done / cancel",
                    d.Length == 0, d.Length == 0 ? "ok" : d);
            }

            // X1-3 wizard 游戏按键: 开火 / 开镜 / 跳跃 not asked; lean / free look / 强化 / 丢雷 / 换弹 still are
            {
                string wz = X1Src("WizardForm.cs");
                string keys = X1Between(wz, "private Control StepGameKeys()", "private Control StepHotkeys()");
                bool gone = !keys.Contains("ActionFire") && !keys.Contains("ActionAds") && !keys.Contains("ActionJump");
                bool kept = keys.Contains("ActionLeanLeft") && keys.Contains("ActionLeanRight") && keys.Contains("ActionFreeLook")
                    && keys.Contains("ActionBoost") && keys.Contains("GrenadeKey") && keys.Contains("AmmoReloadKey");
                bool note = keys.Contains("开火 = 鼠标左键，开镜 = 鼠标右键，跳跃 = 空格");
                // the draft keeps the live fire / ADS / jump keys, so Apply never changes them
                GlobalSettings live = G(); live.Keys.Jump = 0x43;
                WizardModel m = new WizardModel(live);
                m.Draft.MustReadAck = true;
                m.Apply(live);
                bool same = live.Keys.Fire == 0x01 && live.Keys.Ads == 0x02 && live.Keys.Jump == 0x43;
                GlobalSettings fresh = new GlobalSettings();
                bool defaults = fresh.Keys.Fire == 0x01 && fresh.Keys.Ads == 0x02 && fresh.Keys.Jump == 0x20;
                Check("X1-3 wizard 游戏按键: 开火 / 开镜 / 跳跃 not asked (defaults 左键 / 右键 / 空格, a user's own keys kept), the other game keys still asked",
                    gone && kept && note && same && defaults, "gone=" + gone + " kept=" + kept + " note=" + note + " same=" + same + " defaults=" + defaults);
            }

            // X1-4 wizard 画面比例: no stretch question; one line says 拉伸 with the factor; Apply never writes ResStretched
            {
                string wz = X1Src("WizardForm.cs");
                string asp = X1Between(wz, "private Control StepAspect()", "private RadioButton Radio(string text)");
                bool noAsk = !asp.Contains("StretchPicture") && !asp.Contains("RadioButton") && !asp.Contains("有没有被拉宽？") && asp.Contains("Model.AspectLine");
                GlobalSettings g = G(); WizardModel m = new WizardModel(g);
                m.Draft.ResGameW = 1680; m.Draft.ResGameH = 1050; m.Draft.ResAspect = "4:3";
                bool stretched = m.AspectLine.Contains("横向 ×1.20（拉伸）") && m.AspectLine.Contains("铺满");
                m.Draft.ResStretched = false;
                bool bars = m.AspectLine.Contains("有黑边") && m.AspectLine.Contains("常规");
                m.Draft.ResAspect = "16:10";
                bool native = m.AspectLine == "画面比例和分辨率一致，不会被拉宽。";
                GlobalSettings a = G(); a.ResStretched = false;
                WizardModel ma = new WizardModel(a); ma.Draft.ResGameW = 1680; ma.Draft.ResGameH = 1050; ma.Draft.ResAspect = "4:3"; ma.Draft.MustReadAck = true;
                ma.Draft.ResStretched = true; // not a wizard field: must not be copied
                ma.Apply(a);
                GlobalSettings b = G();
                WizardModel mb = new WizardModel(b); mb.Draft.ResGameW = 1680; mb.Draft.ResGameH = 1050; mb.Draft.ResAspect = "4:3"; mb.Draft.MustReadAck = true;
                mb.Apply(b);
                bool kept = a.ResStretched == false && b.ResStretched == null && b.PictureStretched && a.WizardDone && b.WizardDone;
                string model = X1Src("WizardModel.cs");
                bool noReflect = !model.Contains("WriteStretched") && !model.Contains("public bool? Stretched");
                Check("X1-4 wizard 画面比例 (F1): no 「画面有没有被拉宽」 question, line shows 横向 ×1.20（拉伸）/ 有黑边 from 常规 / same ratio; Apply leaves ResStretched (default null = 拉伸)",
                    noAsk && stretched && bars && native && kept && noReflect,
                    "noAsk=" + noAsk + " stretched=" + stretched + " bars=" + bars + " native=" + native + " kept=" + kept + " noReflect=" + noReflect);
            }

            // X1-5 常规: the 有黑边 choice only with 高级设置 on
            {
                string gen = X1Src("SettingsForm.PageGeneral.cs");
                bool adv = gen.Contains("stretchRow.Visible = nonNative && Advanced;") && gen.Contains("stretchL.Visible = stretchRow.Visible;");
                bool note = !gen.Contains("看一眼游戏里的圆形东西") && gen.Contains("打开高级设置选「没拉宽，上下有黑边」或「没拉宽，左右有黑边」");
                string help = HelpText.Body("stretch", G()) + HelpText.Body("aspect", G());
                bool helpOk = help.Contains("默认是「拉宽了」") && help.Contains("高级设置") && !help.Contains("不确定");
                Check("X1-5 常规 (F1): 「画面有没有被拉宽」 only in 高级设置 (default 拉宽了); note and 「?」 texts say so",
                    adv && note && helpOk, "adv=" + adv + " note=" + note + " help=" + helpOk);
            }

            // X1-6 check boxes / radio buttons drawn by Theme: filled accent box + dark tick, hollow box off; radio dot / hollow ring;
            // glyph about the font height; the system glyph is painted over (one hook per control)
            {
                string th = X1Src("Theme.cs");
                string style = X1Between(th, "if (c is CheckBox || c is RadioButton)", "RichTextBox rtb = c as RichTextBox;");
                string paint = X1Between(th, "private static void OnCheckGlyphPaint(", "internal static void Exclusive(");
                string hook = X1Between(th, "private static void HookCheckGlyph(", "private static void OnCheckGlyphPaint(");
                bool hooked = style.Contains("HookCheckGlyph(bb);") && hook.Contains("MarkOnce(GlyphHooked, bb)") && hook.Contains("bb.Paint += OnCheckGlyphPaint;")
                    && hook.Contains("Appearance.Normal");
                bool box = paint.Contains("g.FillRectangle(f, box);") && paint.Contains("new Pen(ColOnAccent") && paint.Contains("g.DrawLines(p, new PointF[]");
                bool radio = paint.Contains("g.FillEllipse(dot, ") && paint.Contains("g.DrawEllipse(p, ring);");
                bool off = paint.Contains(": ColText;") && paint.Contains("ColMuted");
                bool size = th.Contains("Math.Max(12, (int)Math.Round(fontHeight * 0.8))");
                bool cs5 = !Regex.IsMatch(paint + hook, @"\$""|\?\.[A-Za-z]|=>|nameof\(") && !paint.Contains("DashStyle.Dot");
                Check("X1-6 check box / radio (F2): owner-drawn glyph -- ON filled accent + dark tick / accent dot, OFF hollow light frame / ring, disabled muted, ~0.8 font height",
                    hooked && box && radio && off && size && cs5, "hook=" + hooked + " box=" + box + " radio=" + radio + " off=" + off + " size=" + size + " cs5=" + cs5);
            }

            // X1-7 radio groups really exclusive: wizard 开镜方式 / 强化方式 use Theme.Exclusive; no radio pair split across parents
            // without it; 强化方式 shows both 切换 and 按住
            {
                string th = X1Src("Theme.cs"), wz = X1Src("WizardForm.cs");
                string ex = X1Between(th, "internal static void Exclusive(", "private static void Preserve(");
                bool helper = ex.Contains("if (!me.Checked) return;") && ex.Contains("o.Checked = false;");
                string ads = X1Between(wz, "private Control StepAds()", "private Control StepGameKeys()");
                string hot = X1Between(wz, "private Control StepHotkeys()", "private Control StepAutostart()");
                bool adsEx = ads.Contains("Theme.Exclusive(hold, tog);") && ads.Contains("opts.Controls.Add(hold);") && ads.Contains("opts.Controls.Add(tog);");
                bool boost = hot.Contains("Theme.Exclusive(tog, hold);") && hot.Contains("radios.Controls.Add(tog); radios.Controls.Add(hold);")
                    && hot.Contains("Radio(\"切换（按一下开，再按一下关）\")") && hot.Contains("Radio(\"按住（按住时开，松开就关）\")");
                // every wizard step with radio buttons names them in an Exclusive group
                int radios = Regex.Matches(wz, @"RadioButton \w+ = Radio\(").Count, grouped = Regex.Matches(wz, @"Theme\.Exclusive\(\w+, \w+\)").Count * 2;
                bool all = radios == grouped;
                Check("X1-7 radio groups (F2/F3): wizard 开镜方式 + 强化方式 exclusive even across containers, both 强化方式 choices (切换 / 按住) present",
                    helper && adsEx && boost && all, "helper=" + helper + " ads=" + adsEx + " boost=" + boost + " radios=" + radios + " grouped=" + grouped);
            }

            // X1-8 常规 (B5): no 调参分辨率 / 调参比例 / 调参 FOV rows; the stored fields stay and still read / convert
            {
                string gen = X1Src("SettingsForm.PageGeneral.cs");
                bool gone = !gen.Contains("\"调参分辨率\"") && !gen.Contains("\"调参比例\"") && !gen.Contains("\"调参 FOV\"") && !gen.Contains("调参基准（高级）");
                GlobalSettings c; string err;
                GlobalSettings src = G(); src.ResBaseW = 2560; src.ResBaseH = 1440; src.ResBaseFov = 60;
                bool read = SettingsIO.TryParse(X1File(src, null), out c, out err)
                    && c.ResBaseW == 2560 && c.ResBaseH == 1440 && c.ResBaseFov == 60;
                bool kept = SettingsIO.Serialize(c).Contains("\"ResBaseW\"");
                Check("X1-8 常规 (B5): 调参基准 rows removed from the page; ResBaseW / ResBaseH / ResBaseFov still read and saved",
                    gone && read && kept, "gone=" + gone + " read=" + read + " kept=" + kept + " " + err);
            }

            // X1-9 画面有没有被拉宽: 3 choices in ResBars (none / topbottom / leftright), ResStretched kept in step, old files import
            {
                GlobalSettings d = new GlobalSettings();
                bool def = d.ResBars == GlobalSettings.BarsNone && d.PictureStretched;
                GlobalSettings o; string err;
                GlobalSettings s1 = G(); s1.ResGameW = 1680; s1.ResGameH = 1050; s1.ResAspect = "4:3"; s1.ResStretched = false;
                bool p1 = SettingsIO.TryParse(X1File(s1, "ResBars"), out o, out err);
                bool oldNarrow = p1 && o.ResBars == GlobalSettings.BarsLeftRight && !o.PictureStretched;
                GlobalSettings w;
                GlobalSettings s2 = G(); s2.ResGameW = 1920; s2.ResGameH = 1080; s2.ResAspect = "21:9"; s2.ResStretched = false;
                bool p2 = SettingsIO.TryParse(X1File(s2, "ResBars"), out w, out err);
                bool oldWide = p2 && w.ResBars == GlobalSettings.BarsTopBottom;
                GlobalSettings n;
                bool p3 = SettingsIO.TryParse("{\"ResStretched\": null, \"Profiles\": [{\"Name\": \"a\"}]}", out n, out err);
                bool oldNull = p3 && n.ResBars == GlobalSettings.BarsNone && n.PictureStretched;
                GlobalSettings x = new GlobalSettings(); x.ResGameW = 1680; x.ResGameH = 1050; x.ResAspect = "4:3";
                x.SetBars(GlobalSettings.BarsTopBottom);
                bool sync1 = x.ResStretched == false && !x.PictureStretched;
                GlobalSettings rt;
                bool round = SettingsIO.TryParse(SettingsIO.Serialize(x), out rt, out err) && rt.ResBars == GlobalSettings.BarsTopBottom && rt.ResStretched == false;
                x.SetBars(GlobalSettings.BarsNone);
                bool sync2 = x.ResStretched == true && x.PictureStretched;
                x.SetBars("junk");
                bool junk = x.ResBars == GlobalSettings.BarsNone;
                GlobalSettings bad;
                bool p4 = SettingsIO.TryParse("{\"ResBars\": \"sideways\", \"ResStretched\": true, \"Profiles\": [{\"Name\": \"a\"}]}", out bad, out err);
                bool fixedBad = p4 && bad.ResBars == GlobalSettings.BarsNone;
                string gen = X1Src("SettingsForm.PageGeneral.cs");
                bool ui = gen.Contains("\"拉宽了，铺满屏幕\", \"没拉宽，上下有黑边\", \"没拉宽，左右有黑边\"") && gen.Contains("G.SetBars(v);");
                Check("X1-9 画面有没有被拉宽: 3 choices (拉宽了 / 上下有黑边 / 左右有黑边) stored in ResBars, ResStretched in step, old files (false -> the side the ratio leaves empty, null -> 拉宽了) import, junk fixed",
                    def && oldNarrow && oldWide && oldNull && sync1 && round && sync2 && junk && fixedBad && ui,
                    "def=" + def + " narrow=" + oldNarrow + " wide=" + oldWide + " null=" + oldNull + " sync=" + sync1 + "/" + sync2 + " round=" + round + " junk=" + junk + " bad=" + fixedBad + " ui=" + ui);
            }

            // X1-10 常规 画面比例 「自定义」: choosing it opens the W:H box (even when the typed ratio is a list ratio), typing applies
            // as soon as it reads, bad text is refused on leave / Enter, the value is saved (ResAspect = ResAspectCustom)
            {
                string gen = X1Src("SettingsForm.PageGeneral.cs");
                bool mode = gen.Contains("customBox.Visible = customMode || AspectChoiceOf(G.ResAspect) == \"custom\";")
                    && gen.Contains("return customMode ? \"custom\" : AspectChoiceOf(");
                bool typed = gen.Contains("customBox.TextChanged += delegate { if (!loading && customBox.Focused) takeCustom(false); };")
                    && gen.Contains("customBox.Leave += delegate { takeCustom(true); };") && gen.Contains("e2.KeyCode == Keys.Enter");
                bool saved = gen.Contains("G.ResAspectCustom = n; G.ResAspect = n;") && gen.Contains("showFactor(); app.Changed();");
                GlobalSettings g; string err;
                GlobalSettings s3 = G(); s3.ResGameW = 1920; s3.ResGameH = 1080; s3.ResAspect = "123:111"; s3.ResAspectCustom = "123:111";
                bool conv = SettingsIO.TryParse(X1File(s3, null), out g, out err)
                    && g.ResAspect == "123:111" && Math.Abs(ViewScale.StretchX(g.ResGameW, g.ResGameH, g.ResAspect) - (16.0 / 9.0) / (123.0 / 111.0)) < 1e-6;
                Check("X1-10 常规 画面比例 自定义: W:H box opens on 自定义, typed value applied (red while unreadable), saved and used by the conversion (123:111 -> X ×1.60)",
                    mode && typed && saved && conv, "mode=" + mode + " typed=" + typed + " saved=" + saved + " conv=" + conv + " " + err);
            }
        }
    }
}
