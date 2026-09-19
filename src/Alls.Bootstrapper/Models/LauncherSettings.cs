using System.Text.Json.Serialization;

namespace Alls.Bootstrapper.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WindowMode { Fullscreen, Windowed }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DisplayLayoutMode
{
    Auto,
    Chunithm,
    MaimaiDx,
    Ongeki,
    CardMaker,
    Landscape,
    Cabinet
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StartupMode { DefaultGame, Menu }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperationKind { Command, Exit, Shutdown, Restart }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpdateSourceKind { Http, Usb }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpdateApplyMode { FullReplace, ReplaceExisting, AddNewOnly }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameExitBehavior { Menu, Error }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetMatchMode { Any, All }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetLossMode { AnyMissing, AllMissing }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HidDeviceProfile { SegaIo4, AdxHid, NProDx, Maimoller }

public sealed class LauncherSettings
{
    public string PlatformName { get; set; } = "ALLS HX2.1";
    public string Language { get; set; } = "zh-CN";
    public string LogoPath { get; set; } = "Assets/logo.png";
    public string LoadingPath { get; set; } = "Assets/logomode_load.gif";
    public DisplaySettings Display { get; set; } = new();
    public StartupSettings Startup { get; set; } = new();
    public InputSettings Input { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public List<BootPhase> Timeline { get; set; } = CreateDefaultTimeline();
    public List<GameSettings> Games { get; set; } = [GameSettings.CreateDefault()];
    public List<OperationSettings> Operations { get; set; } =
    [
        OperationSettings.CreateShutdown(),
        OperationSettings.CreateRestart(),
        OperationSettings.CreateExit()
    ];
    public List<UpdateSourceSettings> UpdateSources { get; set; } = [];

    public static List<BootPhase> CreateDefaultTimeline() =>
    [
        new() { Step = 1, MessageKey = "STEP_01_MESSAGE", DurationMs = 2200 },
        new() { Step = 4, MessageKey = "STEP_04_MESSAGE", DurationMs = 2200 },
        new() { Step = 21, MessageKey = "STEP_21_MESSAGE", DurationMs = 2400 },
        new() { Step = 30, MessageKey = "STEP_30_MESSAGE", DurationMs = 1200, Action = BootPhaseAction.Launch }
    ];
}

public sealed class DisplaySettings
{
    public WindowMode Mode { get; set; } = WindowMode.Fullscreen;
    public DisplayLayoutMode LayoutMode { get; set; } = DisplayLayoutMode.MaimaiDx;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int StepTransitionMs { get; set; }
    public bool Topmost { get; set; } = true;
    public bool HideCursor { get; set; } = true;
    public bool AllowEscapeToExit { get; set; } = true;
}

public sealed class StartupSettings
{
    public StartupMode Mode { get; set; } = StartupMode.DefaultGame;
    public string DefaultGameId { get; set; } = "maimai-dx";
    public bool AllowSelectToOpenMenu { get; set; } = true;
}

public sealed class LaunchSettings
{
    public bool Enabled { get; set; } = true;
    public string? File { get; set; }
    public List<string> Candidates { get; set; } = ["start.bat", "启动.bat"];
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = ".";
    public bool WaitForExit { get; set; }
}

public sealed class GameSettings
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DisplayLayoutMode? LayoutMode { get; set; }
    public LaunchSettings Launch { get; set; } = new();
    public ProcessMonitorSettings Monitor { get; set; } = new();
    public GameUpdateSettings Update { get; set; } = new();
    public List<BootPhase> Timeline { get; set; } = [];
    public GameExitBehavior OnExit { get; set; } = GameExitBehavior.Menu;
    public string ExitErrorTitle { get; set; } = "GAME PROGRAM ENDED";
    public string ExitErrorMessage { get; set; } = "游戏程序已经停止运行";

    public static GameSettings CreateDefault() => new()
    {
        Id = "maimai-dx",
        Title = "maimai DX",
        Description = "启动 maimai DX 游戏程序",
        LayoutMode = DisplayLayoutMode.MaimaiDx,
        Monitor = new ProcessMonitorSettings
        {
            ProcessNames = ["Sinmai"],
            Window = new WindowTargetSettings { Enabled = true, ProcessName = "Sinmai" },
            ReadyMode = TargetMatchMode.All,
            ExitMode = TargetLossMode.AnyMissing
        }
    };
}

public sealed class OperationSettings
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public OperationKind Kind { get; set; } = OperationKind.Command;
    public LaunchSettings Command { get; set; } = new();
    public ConfirmationSettings Confirmation { get; set; } = new();
    public bool CloseAfterRun { get; set; }

    public static OperationSettings CreateShutdown() => new()
    {
        Id = "shutdown",
        Title = "关闭机台电源",
        Description = "关闭 Windows 与机台电源",
        Kind = OperationKind.Shutdown,
        Command = new LaunchSettings { Enabled = true },
        Confirmation = new ConfirmationSettings { Enabled = true }
    };

    public static OperationSettings CreateRestart() => new()
    {
        Id = "restart",
        Title = "重新启动机台",
        Description = "重新启动 Windows",
        Kind = OperationKind.Restart,
        Command = new LaunchSettings { Enabled = true },
        Confirmation = new ConfirmationSettings { Enabled = true }
    };

    public static OperationSettings CreateExit() => new()
    {
        Id = "exit",
        Title = "退出启动器",
        Description = "关闭 ALLS Bootstrapper",
        Kind = OperationKind.Exit
    };
}

public sealed class ConfirmationSettings
{
    public bool Enabled { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class ProcessMonitorSettings
{
    public List<string> ProcessNames { get; set; } = [];
    public WindowTargetSettings Window { get; set; } = new();
    public TargetMatchMode ReadyMode { get; set; } = TargetMatchMode.All;
    public TargetLossMode ExitMode { get; set; } = TargetLossMode.AnyMissing;
    public int StartupTimeoutMs { get; set; } = 120_000;
    public int PollIntervalMs { get; set; } = 500;
    public int ReadyDelayMs { get; set; } = 5_000;
}

public sealed class GameUpdateSettings
{
    public bool Enabled { get; set; }
    public List<string> SourceIds { get; set; } = [];
    public UpdateApplyMode ApplyMode { get; set; } = UpdateApplyMode.ReplaceExisting;
    public string TargetDirectory { get; set; } = ".";
    public string VersionFile { get; set; } = "ABU_VERSION";
}

public sealed class UpdateSourceSettings
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public UpdateSourceKind Kind { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DriveLetter { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool ContainsMultipleGames { get; set; }
    public int RequestTimeoutMs { get; set; } = 300_000;
}

public sealed class WindowTargetSettings
{
    public bool Enabled { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string TitleContains { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
}

public sealed class InputSettings
{
    public bool Enabled { get; set; } = true;
    public bool KeyboardFallback { get; set; } = true;
    public int HotPlugIntervalMs { get; set; } = 1000;
    public List<HidDeviceSettings> Devices { get; set; } = CreateDefaultDevices();

    private static List<HidDeviceSettings> CreateDefaultDevices() =>
    [
        new() { Name = "SEGA IO4", Profile = HidDeviceProfile.SegaIo4, VendorId = "0x0CA3", ProductIds = ["0x0021"] },
        new() { Name = "SEGA IO4 second device", Profile = HidDeviceProfile.SegaIo4, Player = 2, DeviceIndex = 1, VendorId = "0x0CA3", ProductIds = ["0x0021"] },
        new() { Name = "ADX HID 1P", Profile = HidDeviceProfile.AdxHid, Player = 1, VendorId = "0x2E3C", ProductIds = ["0x5750"] },
        new() { Name = "ADX HID 2P", Profile = HidDeviceProfile.AdxHid, Player = 2, VendorId = "0x2E4C", ProductIds = ["0x5750"] },
        new() { Name = "NPro DX 1P", Profile = HidDeviceProfile.NProDx, Player = 1, VendorId = "0x2E3C", ProductIds = ["0x5751"] },
        new() { Name = "NPro DX 2P", Profile = HidDeviceProfile.NProDx, Player = 2, VendorId = "0x2E3C", ProductIds = ["0x5752"] },
        new() { Name = "Maimoller IO V2 1P", Profile = HidDeviceProfile.Maimoller, Player = 1, VendorId = "0x0E8F", ProductIds = ["0x1224"] },
        new() { Name = "Maimoller IO V2 2P", Profile = HidDeviceProfile.Maimoller, Player = 2, VendorId = "0x0E8F", ProductIds = ["0x1224"] }
    ];
}

public sealed class HidDeviceSettings
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public HidDeviceProfile Profile { get; set; }
    public int Player { get; set; }
    public int DeviceIndex { get; set; }
    public string VendorId { get; set; } = string.Empty;
    public List<string> ProductIds { get; set; } = [];
}

public sealed class LoggingSettings
{
    public bool Enabled { get; set; } = true;
    public string File { get; set; } = "Logs/alls-launcher.log";
}
