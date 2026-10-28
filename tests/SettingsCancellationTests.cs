// 用途：通过实际设置读写验证取消开镜键的默认值、兼容旧配置和保存行为。
using System;
using TarkovCrosshair;

public static class SettingsCancellationTests
{
    // 从保存和读取入口检查默认值及取消键列表的兼容规则。
    public static int Run()
    {
        string json = SettingsIO.Serialize(new GlobalSettings());
        if (!json.Contains("\"CancelAdsKeys\""))
            throw new Exception("默认设置没有保存取消开镜键");
        AssertKeys(Parse(json), new int[] { 9, 27 }, "默认设置保存读取");
        AssertKeys(Parse("{}"), new int[] { 9, 27 }, "旧配置缺少按键设置");
        AssertKeys(Parse("{\"Keys\":null}"), new int[] { 9, 27 }, "按键设置空值补默认");
        AssertKeys(Parse("{\"Keys\":{}}"), new int[] { 9, 27 }, "旧配置缺少取消键");
        AssertKeys(Parse("{\"Keys\":{\"CancelAdsKeys\":null}}"), new int[] { 9, 27 }, "空值补默认取消键");
        AssertKeys(Parse("{\"Keys\":{\"CancelAdsKeys\":[]}}"), new int[0], "显式空数组禁用取消键");
        AssertKeys(Parse("{\"Keys\":{\"CancelAdsKeys\":[0,255,-1,84,27,84,254,1]}}"),
            new int[] { 84, 27, 254, 1 }, "剔除无效重复按键并保留次序");
        AssertKeys(Parse("{\"Keys\":{\"CancelAdsKeys\":[0,-1,255]}}"), new int[0], "全部无效时不偷偷补回默认键");
        return 9 + CheckCopiesAndReset();
    }

    // 设置发布到渲染线程时复制按键，修改副本不能反过来改变原设置。
    private static int CheckCopiesAndReset()
    {
        GlobalSettings original = new GlobalSettings();
        original.AdsMode = "toggle";
        original.Keys.CancelAdsKeys = new int[] { 84, 89 };
        GlobalSettings copy = original.Clone(false);
        copy.Keys.CancelAdsKeys[0] = 85;
        AssertKeys(original, new int[] { 84, 89 }, "全局设置副本相互独立");

        KeyBindings keysCopy = original.Keys.Clone();
        keysCopy.CancelAdsKeys[0] = 86;
        AssertKeys(original, new int[] { 84, 89 }, "按键配置副本相互独立");
        GlobalSettings loaded = Parse(SettingsIO.Serialize(original));
        AssertKeys(loaded, new int[] { 84, 89 }, "自定义多个取消键保存读取");
        if (loaded.AdsMode != "toggle") throw new Exception("保存取消键时改变了开镜方式");

        original.ResetKeys();
        AssertKeys(original, new int[] { 9, 27 }, "恢复默认按键");
        original.Keys.CancelAdsKeys = new int[0];
        AssertKeys(Parse(SettingsIO.Serialize(original)), new int[0], "禁用取消键后保存读取");
        original.Keys.CancelAdsKeys = null;
        copy = original.Clone(false);
        AssertKeys(copy, new int[] { 9, 27 }, "空值复制时补默认键");
        return 7;
    }

    // 通过项目实际读取入口触发旧配置兼容和校验。
    private static GlobalSettings Parse(string json)
    {
        GlobalSettings settings;
        string error;
        if (!SettingsIO.TryParse(json, out settings, out error))
            throw new Exception("读取测试设置失败：" + error);
        return settings;
    }

    // 期望值来自约定的默认键和具体配置，不重复实现生产校验规则。
    private static void AssertKeys(GlobalSettings settings, int[] expected, string scenario)
    {
        int[] actual = settings.Keys.CancelAdsKeys;
        if (actual == null || actual.Length != expected.Length)
            throw new Exception(scenario + "：取消键数量不符合预期");
        for (int i = 0; i < expected.Length; i++)
            if (actual[i] != expected[i]) throw new Exception(scenario + "：取消键内容不符合预期");
    }
}
