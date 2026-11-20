// TarkovShade - Chinese labels and hover tooltips for the ShadePilot-style settings.
// "Help" texts marked [SP] are ShadePilot's own tooltip wording, copied from ShadePilot
// DisplayPreset.cs MainForm SetHelp calls (lines 829-859, commit 2bb77f6).
// ShadePilot: https://github.com/BYS-XSQ/ShadePilot, Copyright (c) 2026 BYS-XSQ, MIT License
// (see LICENSE-SHADEPILOT.txt). Texts without [SP] and all "Plain" one-liners are TarkovShade's.
// C# 5; all non-ASCII text is written as \u escapes so csc.exe reads it the same on any code page.
using System;
using System.Collections.Generic;

namespace TarkovShade
{
    public static class PilotHelpText
    {
        // Keys (use PilotStyle property names where there is one).
        public const string Shadows = "Shadows", Highlights = "Highlights", Midtones = "Midtones",
            BlackPoint = "BlackPoint", WhitePoint = "WhitePoint", ContrastPivot = "ContrastPivot",
            Temperature = "Temperature", Tint = "Tint", Vibrance = "Vibrance", Exposure = "Exposure",
            ShadowRange = "ShadowRange", HighlightRange = "HighlightRange",
            TransitionSoftness = "TransitionSoftness", StrengthMode = "StrengthMode",
            Saturation = "Saturation", SaturationBackend = "SaturationBackend",
            DriverVibrance = "DriverVibrance", WindowsSaturation = "WindowsSaturation",
            HardwareBrightness = "HardwareBrightness", HardwareContrast = "HardwareContrast",
            ShareCode = "ShareCode", AdvancedToggle = "AdvancedToggle", UntestedPreset = "UntestedPreset",
            HardwareSharpness = "HardwareSharpness", RemovedNote = "RemovedNote",
            HotkeyToggle = "HotkeyToggle", HotkeyNextPreset = "HotkeyNextPreset",
            HotkeyHoldOriginal = "HotkeyHoldOriginal", HotkeyPreset = "HotkeyPreset",
            HotkeyRestoreAll = "HotkeyRestoreAll", HotkeyRules = "HotkeyRules";

        /// <summary>The 13 style keys in ShadePilot's 3-column grid order (row by row).</summary>
        public static readonly string[] StyleGridOrder = {
            Shadows, Highlights, Midtones,
            BlackPoint, WhitePoint, ContrastPivot,
            Temperature, Tint, Vibrance,
            Exposure, ShadowRange, HighlightRange,
            TransitionSoftness };

        private sealed class Entry
        {
            public string Label, Help, Plain; public bool FromShadePilot;
            public Entry(string label, string help, string plain, bool sp) { Label = label; Help = help; Plain = plain; FromShadePilot = sp; }
        }

        private static readonly Dictionary<string, Entry> map = Build();

        private static Dictionary<string, Entry> Build()
        {
            Dictionary<string, Entry> d = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            d[Shadows] = new Entry("\u63d0\u4eae\u6697\u90e8",
                "\u63d0\u5347\u9634\u5f71\u548c\u8f83\u6697\u7684\u4e2d\u95f4\u533a\u57df\uff0c\u540c\u65f6\u5c3d\u91cf\u4fdd\u7559\u7eaf\u9ed1\u4e0e\u4eae\u90e8\u3002\u9002\u5408\u67e5\u770b\u6697\u5904\u3002",
                "\u628a\u6697\u5904\u8c03\u4eae\uff0c\u770b\u6e05\u9634\u5f71\u91cc\u7684\u4eba\u548c\u4e1c\u897f\u3002", true);
            d[Highlights] = new Entry("\u538b\u4f4e\u9ad8\u5149",
                "\u538b\u4f4e\u753b\u9762\u4e2d\u9ad8\u4eae\u533a\u57df\uff0c\u51cf\u5c11\u706f\u5149\u3001\u5929\u7a7a\u6216\u767d\u8272\u7269\u4f53\u523a\u773c\u3002",
                "\u628a\u592a\u4eae\u7684\u5730\u65b9\u538b\u6697\uff0c\u5929\u7a7a\u548c\u706f\u5149\u4e0d\u518d\u523a\u773c\u3002", true);
            d[Midtones] = new Entry("\u4e2d\u95f4\u8c03",
                "\u8c03\u6574\u753b\u9762\u4e3b\u4f53\u6240\u5728\u7684\u4e2d\u95f4\u4eae\u5ea6\u8303\u56f4\uff1b\u6b63\u503c\u63d0\u4eae\uff0c\u8d1f\u503c\u538b\u6697\u3002",
                "\u8c03\u6574\u753b\u9762\u5927\u90e8\u5206\u533a\u57df\u7684\u660e\u6697\uff0c\u6b63\u503c\u66f4\u4eae\uff0c\u8d1f\u503c\u66f4\u6697\u3002", true);
            d[BlackPoint] = new Entry("\u9ed1\u4f4d",
                "\u8c03\u6574\u9ed1\u573a\u7aef\u70b9\u3002\u6b63\u503c\u62ac\u8d77\u9ed1\u4f4d\u3001\u663e\u9732\u6700\u6697\u7ec6\u8282\uff1b\u8d1f\u503c\u8ba9\u9ed1\u8272\u66f4\u6df1\u3002",
                "\u6b63\u503c\u8ba9\u6700\u9ed1\u7684\u5730\u65b9\u53d8\u7070\u4e00\u70b9\u3001\u80fd\u770b\u51fa\u7ec6\u8282\uff1b\u8d1f\u503c\u8ba9\u9ed1\u8272\u66f4\u9ed1\u3002", true);
            d[WhitePoint] = new Entry("\u767d\u4f4d",
                "\u8c03\u6574\u767d\u573a\u7aef\u70b9\u3002\u6b63\u503c\u589e\u5f3a\u6700\u4eae\u90e8\u5206\uff1b\u8d1f\u503c\u6536\u56de\u767d\u573a\u5e76\u4fdd\u7559\u4eae\u90e8\u4f59\u91cf\u3002",
                "\u6b63\u503c\u8ba9\u6700\u4eae\u7684\u5730\u65b9\u66f4\u4eae\uff1b\u8d1f\u503c\u8ba9\u767d\u8272\u6536\u4e00\u70b9\u3001\u4fdd\u7559\u4eae\u90e8\u7ec6\u8282\u3002", true);
            d[ContrastPivot] = new Entry("\u4e2d\u95f4\u5bf9\u6bd4",
                "\u56f4\u7ed5\u4e2d\u95f4\u4eae\u5ea6\u589e\u52a0\u6216\u964d\u4f4e\u66f2\u7ebf\u5bf9\u6bd4\u5ea6\uff0c\u4e0d\u4f1a\u6539\u52a8\u663e\u793a\u5668\u786c\u4ef6\u5bf9\u6bd4\u5ea6\u3002",
                "\u6b63\u503c\u753b\u9762\u66f4\u6709\u5c42\u6b21\u3001\u66f4\u201c\u786c\u201d\uff1b\u8d1f\u503c\u66f4\u67d4\u3001\u66f4\u5e73\u3002", true);
            d[Temperature] = new Entry("\u8272\u6e29",
                "\u8fde\u7eed\u6539\u53d8\u7ea2\u84dd\u901a\u9053\u6bd4\u4f8b\u3002\u8d1f\u503c\u504f\u51b7\u84dd\uff0c\u6b63\u503c\u504f\u6696\u9ec4\u3002",
                "\u8d1f\u503c\u753b\u9762\u504f\u84dd\uff08\u51b7\uff09\uff0c\u6b63\u503c\u504f\u9ec4\uff08\u6696\uff09\u3002", true);
            d[Tint] = new Entry("\u8272\u8c03",
                "\u8fde\u7eed\u6539\u53d8\u7eff\u4e0e\u6d0b\u7ea2\u65b9\u5411\u7684\u8272\u504f\u3002\u8d1f\u503c\u504f\u7eff\uff0c\u6b63\u503c\u504f\u6d0b\u7ea2\u3002",
                "\u8d1f\u503c\u753b\u9762\u504f\u7eff\uff0c\u6b63\u503c\u504f\u7d2b\u7ea2\u3002", true);
            d[Vibrance] = new Entry("\u81ea\u7136\u8272\u5f69",
                "\u5728 Gamma \u66f2\u7ebf\u80fd\u529b\u8303\u56f4\u5185\u8fd1\u4f3c\u589e\u5f3a\u8f83\u67d4\u548c\u7684\u989c\u8272\u3002\u5b83\u4e0d\u662f\u9010\u50cf\u7d20\u6ee4\u955c\u3002",
                "\u8ba9\u989c\u8272\u66f4\u9c9c\u8273\u4e00\u70b9\uff08\u8fd1\u4f3c\u6548\u679c\uff0c\u4f1a\u987a\u5e26\u52a0\u4e00\u70b9\u5bf9\u6bd4\uff09\u3002", true);
            d[Exposure] = new Entry("\u6574\u4f53\u66dd\u5149",
                "\u5728\u7ebf\u6027\u66f2\u7ebf\u8d77\u70b9\u6574\u4f53\u589e\u51cf\u66dd\u5149\uff0c\u8303\u56f4\u7ea6\u4e3a -2 \u5230 +2 \u6863\uff1b\u4e0d\u4f1a\u6539\u53d8\u663e\u793a\u5668\u80cc\u5149\u3002",
                "\u6574\u4e2a\u753b\u9762\u4e00\u8d77\u53d8\u4eae\u6216\u53d8\u6697\u3002", true);
            d[ShadowRange] = new Entry("\u6697\u90e8\u8303\u56f4",
                "\u53ea\u6539\u53d8\u6697\u90e8\u8865\u507f\u8986\u76d6\u8303\u56f4\u3002\u8d1f\u503c\u96c6\u4e2d\u5728\u6700\u9ed1\u533a\u57df\uff0c\u6b63\u503c\u5ef6\u4f38\u5230\u8f83\u6697\u4e2d\u95f4\u8c03\u3002",
                "\u51b3\u5b9a\u201c\u63d0\u4eae\u6697\u90e8\u201d\u7ba1\u591a\u5927\u8303\u56f4\uff1a\u8d1f\u503c\u53ea\u7ba1\u6700\u9ed1\u5904\uff0c\u6b63\u503c\u7ba1\u5f97\u66f4\u5bbd\u3002", true);
            d[HighlightRange] = new Entry("\u9ad8\u5149\u8303\u56f4",
                "\u53ea\u6539\u53d8\u9ad8\u5149\u538b\u5236\u8986\u76d6\u8303\u56f4\u3002\u8d1f\u503c\u96c6\u4e2d\u5728\u6700\u4eae\u533a\u57df\uff0c\u6b63\u503c\u66f4\u65e9\u8fdb\u5165\u4e2d\u9ad8\u4eae\u3002",
                "\u51b3\u5b9a\u201c\u538b\u4f4e\u9ad8\u5149\u201d\u7ba1\u591a\u5927\u8303\u56f4\uff1a\u8d1f\u503c\u53ea\u7ba1\u6700\u4eae\u5904\uff0c\u6b63\u503c\u7ba1\u5f97\u66f4\u5bbd\u3002", true);
            d[TransitionSoftness] = new Entry("\u8fc7\u6e21\u67d4\u548c",
                "\u53ea\u6539\u53d8\u660e\u6697\u8c03\u6574\u8fb9\u7f18\u7684\u8854\u63a5\u3002\u8d1f\u503c\u66f4\u96c6\u4e2d\uff0c\u6b63\u503c\u66f4\u67d4\u548c\u5bbd\u7f13\u3002",
                "\u8ba9\u63d0\u4eae\u548c\u538b\u6697\u7684\u8fb9\u7f18\u66f4\u81ea\u7136\uff0c\u4e0d\u51fa\u73b0\u660e\u663e\u5206\u754c\u3002", true);
            d[StrengthMode] = new Entry("\u5f3a\u5ea6",
                "\u9650\u5236\u753b\u9762\u53c2\u6570\u7684\u53ef\u8c03\u8303\u56f4\uff1a\u67d4\u548c\u9002\u5408\u65e5\u5e38\uff0c\u6807\u51c6\u517c\u987e\u6548\u679c\uff0c\u6781\u9650\u5f00\u653e\u5b8c\u6574\u8303\u56f4\u3002",
                "\u53ea\u9650\u5236\u6ed1\u5757\u80fd\u62c9\u591a\u8fdc\uff0c\u4e0d\u4f1a\u6539\u53d8\u5df2\u8bbe\u597d\u7684\u6570\u503c\u6548\u679c\u3002", true);
            d[Saturation] = new Entry("\u9971\u548c\u5ea6",
                "\u663e\u793a\u5668\u652f\u6301\u65f6\u8c03\u8282 DDC/CI \u786c\u4ef6\u9971\u548c\u5ea6\uff1b\u4e0d\u652f\u6301\u65f6\u81ea\u52a8\u6539\u7528 Windows \u5168\u5c4f\u8f6f\u4ef6\u9971\u548c\u5ea6\u3002",
                "\u6574\u4f53\u989c\u8272\u6d53\u6de1\uff0c50 \u4e0d\u53d8\uff0c\u5f80\u4e0a\u66f4\u6d53\uff0c\u5f80\u4e0b\u66f4\u6de1\u3002", true);
            d[SaturationBackend] = new Entry("\u9971\u548c\u5ea6\u65b9\u5f0f",
                "\u9009\u62e9\u9971\u548c\u5ea6\u7531\u8c01\u6765\u505a\uff1a\u663e\u5361\u9a71\u52a8\u7684\u6570\u5b57\u632f\u52a8\uff08NVIDIA / AMD\uff0c\u63a8\u8350\uff0c\u51e0\u4e4e\u4e0d\u5360\u6027\u80fd\uff09\uff0c\u6216 Windows \u653e\u5927\u955c\u989c\u8272\u77e9\u9635\uff08\u6bcf\u4e00\u5e27\u90fd\u8981\u591a\u7b97\u4e00\u904d\uff0c\u4f1a\u964d\u4f4e\u6e38\u620f\u5e27\u6570\uff0c\u53ea\u5728\u663e\u5361\u9a71\u52a8\u4e0d\u652f\u6301\u65f6\u7528\uff09\u3002",
                "\u9009\u4e00\u79cd\u52a0\u9971\u548c\u5ea6\u7684\u65b9\u6cd5\uff1b\u7528\u663e\u5361\u9a71\u52a8\uff0c\u663e\u5361\u4e0d\u652f\u6301\u65f6\u624d\u7528 Windows \u653e\u5927\u955c\u3002", false);
            d[DriverVibrance] = new Entry("\u663e\u5361\u6570\u5b57\u632f\u52a8",
                "\u4f7f\u7528 NVIDIA / AMD \u9a71\u52a8\u81ea\u5e26\u7684\u6570\u5b57\u632f\u52a8\u8c03\u8282\u9971\u548c\u5ea6\uff0c\u51e0\u4e4e\u4e0d\u5f71\u54cd\u6027\u80fd\u3002",
                "\u7528\u663e\u5361\u9a71\u52a8\u52a0\u989c\u8272\u6d53\u5ea6\uff0c\u901f\u5ea6\u5feb\u3001\u517c\u5bb9\u597d\u3002", false);
            d[WindowsSaturation] = new Entry("Windows \u8f6f\u4ef6\u9971\u548c\u5ea6",
                "\u4f7f\u7528 Windows \u653e\u5927\u955c\u5168\u5c4f\u989c\u8272\u77e9\u9635\u8c03\u8282\u9971\u548c\u5ea6\u3002\u6bcf\u4e00\u5e27\u90fd\u8981\u591a\u7b97\u4e00\u904d\uff0c\u4f1a\u964d\u4f4e\u6e38\u620f\u5e27\u6570\uff1b\u53ef\u80fd\u4e0e DLSS \u5e27\u751f\u6210\u3001AFMF \u7b49\u63d2\u5e27\u51b2\u7a81\u3002",
                "\u7528 Windows \u81ea\u5e26\u529f\u80fd\u52a0\u989c\u8272\u6d53\u5ea6\uff1b\u4f1a\u964d\u4f4e\u5e27\u6570\uff0c\u53ea\u5728\u663e\u5361\u9a71\u52a8\u4e0d\u652f\u6301\u65f6\u7528\u3002", false);
            d[HardwareBrightness] = new Entry("\u4eae\u5ea6",
                "\u8c03\u8282\u663e\u793a\u5668\u80cc\u5149\u4eae\u5ea6\u3002\u5b83\u4f1a\u6539\u53d8\u5c4f\u5e55\u6574\u4f53\u53d1\u5149\u5f3a\u5ea6\uff0c\u4e0d\u7b49\u540c\u4e8e\u63d0\u4eae\u6697\u90e8\u3002",
                "\u76f4\u63a5\u8c03\u663e\u793a\u5668\u672c\u8eab\u7684\u4eae\u5ea6\uff08\u80cc\u5149\uff09\uff0c\u9700\u8981\u663e\u793a\u5668\u652f\u6301 DDC/CI\u3002", true);
            d[HardwareContrast] = new Entry("\u5bf9\u6bd4\u5ea6",
                "\u8c03\u8282\u663e\u793a\u5668\u786c\u4ef6\u5bf9\u6bd4\u5ea6\u3002\u8fc7\u9ad8\u53ef\u80fd\u9020\u6210\u4eae\u90e8\u6216\u6697\u90e8\u7ec6\u8282\u4e22\u5931\u3002",
                "\u76f4\u63a5\u8c03\u663e\u793a\u5668\u672c\u8eab\u7684\u5bf9\u6bd4\u5ea6\uff1b0 \u8868\u793a\u663e\u793a\u5668\u91cf\u7a0b\u4e2d\u70b9\u3002", true);
            d[AdvancedToggle] = new Entry("\u663e\u793a\u9ad8\u7ea7\u8bbe\u7f6e",
                "\u663e\u793a\u767d\u4f4d\u3001\u4e2d\u95f4\u5bf9\u6bd4\u3001\u8272\u6e29\u3001\u8272\u8c03\u3001\u81ea\u7136\u8272\u5f69\u3001\u6574\u4f53\u66dd\u5149\u3001\u9ad8\u5149\u8303\u56f4\u3001\u8fc7\u6e21\u67d4\u548c\u548c\u5f3a\u5ea6\u6a21\u5f0f\u3002",
                "\u4e00\u822c\u4e0d\u7528\u5f00\uff0c\u57fa\u7840\u8bbe\u7f6e\u5df2\u7ecf\u591f\u7528\u3002", false);
            d[UntestedPreset] = new Entry("\u81ea\u5e26\u9884\u8bbe",
                "\u8fd9\u662f\u7a0b\u5e8f\u81ea\u5e26\u7684\u9884\u8bbe\uff0c\u53ef\u4ee5\u76f4\u63a5\u7528\uff0c\u4e5f\u53ef\u4ee5\u6309\u81ea\u5df1\u7684\u663e\u793a\u5668\u518d\u5fae\u8c03\u3002",
                "\u4e0d\u5408\u9002\u5c31\u5fae\u8c03\uff0c\u518d\u53e6\u5b58\u4e3a\u81ea\u5df1\u7684\u9884\u8bbe\u3002", false);
            d[HardwareSharpness] = new Entry("\u663e\u793a\u5668\u9510\u5ea6",
                "\u8c03\u8282\u663e\u793a\u5668\u81ea\u5e26\u7684\u9510\u5316\u3002\u53ea\u6709\u663e\u793a\u5668\u652f\u6301\u65f6\u624d\u663e\u793a\uff1b\u8fc7\u9ad8\u4f1a\u51fa\u73b0\u767d\u8fb9\u548c\u566a\u70b9\u3002",
                "\u8ba9\u753b\u9762\u8fb9\u7f18\u66f4\u6e05\u695a\uff0c\u592a\u9ad8\u4f1a\u6709\u767d\u8fb9\u3002", false);
            d[RemovedNote] = new Entry("\u6765\u81ea\u5206\u4eab\u7801",
                "\u8fd9\u4e2a\u503c\u6765\u81ea\u5bfc\u5165\u7684\u5206\u4eab\u7801\uff0c\u4f1a\u7167\u5e38\u751f\u6548\uff1b\u672c\u7a0b\u5e8f\u4e0d\u518d\u63d0\u4f9b\u8fd9\u4e2a\u6ed1\u5757\uff0c\u53ea\u80fd\u67e5\u770b\u3002",
                "\u5bfc\u5165\u65f6\u5e26\u6765\u7684\u8bbe\u7f6e\uff0c\u4f1a\u751f\u6548\u4f46\u4e0d\u80fd\u5728\u8fd9\u91cc\u6539\u3002", false);
            d[HotkeyToggle] = new Entry("\u5f00\u5173\u6ee4\u955c",
                "\u6309\u4e00\u4e0b\u6253\u5f00\u6216\u5173\u95ed\u753b\u9762\u6ee4\u955c\uff08\u9ed8\u8ba4 F8\uff09\u3002",
                "\u4e00\u952e\u5f00\u5173\u6ee4\u955c\u3002", false);
            d[HotkeyNextPreset] = new Entry("\u4e0b\u4e00\u4e2a\u9884\u8bbe",
                "\u6309\u4e00\u4e0b\u5207\u6362\u5230\u4e0b\u4e00\u4e2a\u9884\u8bbe\uff08\u9ed8\u8ba4 F9\uff09\u3002",
                "\u4e00\u952e\u6362\u4e0b\u4e00\u4e2a\u9884\u8bbe\u3002", false);
            d[HotkeyHoldOriginal] = new Entry("\u6309\u4f4f\u770b\u539f\u753b\u9762",
                "\u6309\u4f4f\u65f6\u663e\u793a\u539f\u59cb\u753b\u9762\uff0c\u677e\u5f00\u540e\u6062\u590d\u6ee4\u955c\uff08\u9ed8\u8ba4 F7\uff09\uff0c\u65b9\u4fbf\u5bf9\u6bd4\u3002",
                "\u6309\u4f4f\u770b\u6ca1\u52a0\u6ee4\u955c\u7684\u6837\u5b50\u3002", false);
            d[HotkeyPreset] = new Entry("\u9884\u8bbe\u5feb\u6377\u952e",
                "\u76f4\u63a5\u5207\u6362\u5230\u8fd9\u4e2a\u9884\u8bbe\u3002\u9ed8\u8ba4\u53ea\u6709 \u901a\u7528 = Ctrl+1\u3001\u9ed1\u591c\u6a21\u5f0f = Ctrl+2\uff0c\u5176\u4ed6\u9884\u8bbe\u672a\u5206\u914d\u3002",
                "\u4e00\u952e\u76f4\u8fbe\u8fd9\u4e2a\u9884\u8bbe\u3002", false);
            d[HotkeyRestoreAll] = new Entry("\u5168\u90e8\u6062\u590d\u539f\u6837",
                "\u628a\u989c\u8272\u66f2\u7ebf\u3001\u9971\u548c\u5ea6\u548c\u663e\u793a\u5668\u4eae\u5ea6 / \u5bf9\u6bd4\u5ea6 / \u9510\u5ea6\u5168\u90e8\u6062\u590d\u5230\u6253\u5f00\u7a0b\u5e8f\u524d\u7684\u6837\u5b50\uff0c\u6ee4\u955c\u5173\u6389\u3002\u6ca1\u6709\u5feb\u6377\u952e\uff1b\u9000\u51fa\u7a0b\u5e8f\u65f6\u4e5f\u4f1a\u81ea\u52a8\u6062\u590d\u3002",
                "\u753b\u9762\u51fa\u95ee\u9898\u65f6\u70b9\u8fd9\u91cc\u5168\u90e8\u8fd8\u539f\u3002", false);
            d[HotkeyRules] = new Entry("\u5feb\u6377\u952e\u89c4\u5219",
                "\u53ea\u80fd\u7528 F \u952e\u6216\u5e26 Ctrl/Alt/Shift \u7684\u7ec4\u5408\uff1b\u4e0d\u80fd\u91cd\u590d\uff1b\u4e0d\u80fd\u7528\u5355\u72ec\u7684\u5b57\u6bcd\u3001\u7a7a\u683c\u6216\u56de\u8f66\u3002",
                "\u7528 F \u952e\u6216\u7ec4\u5408\u952e\uff0c\u4e0d\u8981\u91cd\u590d\u3002", false);
            d[ShareCode] = new Entry("\u5206\u4eab\u7801",
                "\u590d\u5236\u8fd9\u5957\u6ee4\u955c\u7684\u5206\u4eab\u7801\uff08TSHADE2 \u5f00\u5934\uff09\u53d1\u7ed9\u670b\u53cb\uff0c\u6216\u7c98\u8d34\u522b\u4eba\u7684\u5206\u4eab\u7801\u5bfc\u5165\u4e3a\u65b0\u9884\u8bbe\u3002",
                "\u590d\u5236\u5206\u4eab\u7801\u53d1\u7ed9\u670b\u53cb\uff0c\u5bf9\u65b9\u7c98\u8d34\u5c31\u80fd\u5f97\u5230\u4e00\u6837\u7684\u6548\u679c\u3002", false);
            return d;
        }

        /// <summary>Chinese slider label, e.g. "\u63d0\u4eae\u6697\u90e8". Unknown key -> the key itself.</summary>
        public static string Label(string key) { Entry e; return map.TryGetValue(key ?? "", out e) ? e.Label : key; }

        /// <summary>Hover tooltip: ShadePilot's own wording where it has one.</summary>
        public static string Help(string key) { Entry e; return map.TryGetValue(key ?? "", out e) ? e.Help : ""; }

        /// <summary>Short plain one-line "\u8c03\u8fd9\u4e2a\u80fd\u5e72\u561b".</summary>
        public static string Plain(string key) { Entry e; return map.TryGetValue(key ?? "", out e) ? e.Plain : ""; }

        /// <summary>Tooltip with both lines: plain one-liner, then the detailed help.</summary>
        public static string Tooltip(string key)
        {
            string p = Plain(key), h = Help(key);
            if (p.Length == 0) return h;
            if (h.Length == 0) return p;
            return p + "\r\n" + h;
        }

        public static bool IsShadePilotWording(string key) { Entry e; return map.TryGetValue(key ?? "", out e) && e.FromShadePilot; }

        /// <summary>Strength mode names: 0 \u67d4\u548c, 1 \u6807\u51c6, 2 \u6781\u9650.</summary>
        public static string StrengthName(int mode) { return mode == 0 ? "\u67d4\u548c" : mode == 1 ? "\u6807\u51c6" : "\u6781\u9650"; }
    }
}
