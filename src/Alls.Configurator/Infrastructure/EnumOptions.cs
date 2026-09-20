using System.Reflection;
using System.Xml.Linq;
using Alls.Bootstrapper.Models;

namespace Alls.Configurator.Infrastructure;

public sealed record EnumOption<T>(T Value, string Label) where T : struct, Enum;

public sealed record StringOption(string Value, string Label);

public sealed record MessageKeyOption(string Key, string Chinese, string English, string Japanese)
{
    public string Label => $"{Key} — {Chinese}";

    public string HelpText => $"简体中文：{Chinese}\nEnglish：{English}\n日本語：{Japanese}";
}

public static class EnumOptions
{
    public static IReadOnlyList<StringOption> GameLanguages { get; } =
    [
        new(string.Empty, "继承全局语言"),
        new("zh-CN", "简体中文（zh-CN）"),
        new("en-US", "English（en-US）"),
        new("ja-JP", "日本語（ja-JP）")
    ];

    public static IReadOnlyList<MessageKeyOption> MessageKeys { get; } = CreateMessageKeyOptions();

    public static IReadOnlyList<EnumOption<WindowMode>> WindowModes { get; } =
    [
        new(WindowMode.Fullscreen, "全屏"),
        new(WindowMode.Windowed, "窗口")
    ];

    public static IReadOnlyList<EnumOption<DisplayLayoutMode>> LayoutModes { get; } =
    [
        new(DisplayLayoutMode.Auto, "自动判断"),
        new(DisplayLayoutMode.Chunithm, "CHUNITHM（横屏）"),
        new(DisplayLayoutMode.MaimaiDx, "maimai DX（竖屏）"),
        new(DisplayLayoutMode.Ongeki, "音击（竖屏）"),
        new(DisplayLayoutMode.CardMaker, "打印机（Card Maker）"),
        new(DisplayLayoutMode.Landscape, "旧版横屏布局"),
        new(DisplayLayoutMode.Cabinet, "旧版机台布局")
    ];

    public static IReadOnlyList<EnumOption<StartupMode>> StartupModes { get; } =
    [
        new(StartupMode.DefaultGame, "直接启动默认游戏"),
        new(StartupMode.Menu, "显示游戏菜单")
    ];

    public static IReadOnlyList<EnumOption<OperationKind>> OperationKinds { get; } =
    [
        new(OperationKind.Command, "运行自定义命令"),
        new(OperationKind.Exit, "退出启动器"),
        new(OperationKind.Shutdown, "关闭机台"),
        new(OperationKind.Restart, "重新启动机台"),
        new(OperationKind.UpdateAllGames, "更新所有游戏")
    ];

    public static IReadOnlyList<EnumOption<UpdateSourceKind>> UpdateSourceKinds { get; } =
    [
        new(UpdateSourceKind.Http, "HTTP 文件服务器"),
        new(UpdateSourceKind.Usb, "U 盘 / 本地目录")
    ];

    public static IReadOnlyList<EnumOption<UpdateApplyMode>> UpdateApplyModes { get; } =
    [
        new(UpdateApplyMode.FullReplace, "完整替换目标目录"),
        new(UpdateApplyMode.ReplaceExisting, "替换已有文件并添加新文件"),
        new(UpdateApplyMode.AddNewOnly, "仅添加不存在的文件")
    ];

    public static IReadOnlyList<EnumOption<GameExitBehavior>> GameExitBehaviors { get; } =
    [
        new(GameExitBehavior.Menu, "返回游戏菜单"),
        new(GameExitBehavior.Error, "显示错误页面")
    ];

    public static IReadOnlyList<EnumOption<TargetMatchMode>> TargetMatchModes { get; } =
    [
        new(TargetMatchMode.Any, "任一目标出现即就绪"),
        new(TargetMatchMode.All, "所有目标出现才就绪")
    ];

    public static IReadOnlyList<EnumOption<TargetLossMode>> TargetLossModes { get; } =
    [
        new(TargetLossMode.AnyMissing, "任一目标消失即结束"),
        new(TargetLossMode.AllMissing, "所有目标消失才结束")
    ];

    public static IReadOnlyList<EnumOption<HidDeviceProfile>> HidDeviceProfiles { get; } =
    [
        new(HidDeviceProfile.SegaIo4, "SEGA IO4"),
        new(HidDeviceProfile.AdxHid, "ADX HID"),
        new(HidDeviceProfile.NProDx, "NPro DX"),
        new(HidDeviceProfile.Maimoller, "Maimoller IO V2")
    ];

    public static IReadOnlyList<EnumOption<BootPhaseAction>> BootPhaseActions { get; } =
    [
        new(BootPhaseAction.Continue, "继续下一阶段"),
        new(BootPhaseAction.Launch, "启动游戏"),
        new(BootPhaseAction.Exit, "退出启动器")
    ];

    private static IReadOnlyList<MessageKeyOption> CreateMessageKeyOptions()
    {
        var chinese = LoadMessages("zh-CN");
        var english = LoadMessages("en-US");
        var japanese = LoadMessages("ja-JP");
        return chinese
            .Select(entry => new MessageKeyOption(
                entry.Key,
                entry.Value,
                english.GetValueOrDefault(entry.Key, "（无对应文本）"),
                japanese.GetValueOrDefault(entry.Key, "（无对应文本）")))
            .ToList();
    }

    private static Dictionary<string, string> LoadMessages(string language)
    {
        var resourceName = $"Alls.Configurator.Resources.Strings.{language}.xaml";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"找不到内置语言资源：{resourceName}");
        var document = XDocument.Load(stream);
        var keyName = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        return document.Root?.Elements()
            .Select(element => new { Key = element.Attribute(keyName)?.Value, Value = element.Value })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .ToDictionary(entry => entry.Key!, entry => entry.Value, StringComparer.Ordinal)
            ?? [];
    }
}
