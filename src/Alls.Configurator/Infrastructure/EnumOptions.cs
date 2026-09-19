using Alls.Bootstrapper.Models;

namespace Alls.Configurator.Infrastructure;

public sealed record EnumOption<T>(T Value, string Label) where T : struct, Enum;

public static class EnumOptions
{
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
}
