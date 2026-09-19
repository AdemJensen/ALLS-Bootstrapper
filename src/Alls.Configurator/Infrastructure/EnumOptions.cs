using Alls.Bootstrapper.Models;

namespace Alls.Configurator.Infrastructure;

public static class EnumOptions
{
    public static Array WindowModes { get; } = Enum.GetValues<WindowMode>();
    public static Array LayoutModes { get; } = Enum.GetValues<DisplayLayoutMode>();
    public static Array StartupModes { get; } = Enum.GetValues<StartupMode>();
    public static Array OperationKinds { get; } = Enum.GetValues<OperationKind>();
    public static Array UpdateSourceKinds { get; } = Enum.GetValues<UpdateSourceKind>();
    public static Array UpdateApplyModes { get; } = Enum.GetValues<UpdateApplyMode>();
    public static Array GameExitBehaviors { get; } = Enum.GetValues<GameExitBehavior>();
    public static Array TargetMatchModes { get; } = Enum.GetValues<TargetMatchMode>();
    public static Array TargetLossModes { get; } = Enum.GetValues<TargetLossMode>();
    public static Array HidDeviceProfiles { get; } = Enum.GetValues<HidDeviceProfile>();
    public static Array BootPhaseActions { get; } = Enum.GetValues<BootPhaseAction>();
}
