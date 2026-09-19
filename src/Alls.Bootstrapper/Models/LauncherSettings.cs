using System.Text.Json.Serialization;

namespace Alls.Bootstrapper.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WindowMode
{
    Fullscreen,
    Windowed
}

public sealed class LauncherSettings
{
    public string PlatformName { get; set; } = "ALLS HX2.1";

    public string Language { get; set; } = "zh-CN";

    public string LogoPath { get; set; } = "Assets/logo.png";

    public string LoadingPath { get; set; } = "Assets/logomode_load.gif";

    public bool AutoCloseAfterSequence { get; set; } = true;

    public DisplaySettings Display { get; set; } = new();

    public LaunchSettings Launch { get; set; } = new();

    public LoggingSettings Logging { get; set; } = new();

    public List<BootPhase> Timeline { get; set; } = CreateDefaultTimeline();

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

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public bool Topmost { get; set; } = true;

    public bool HideCursor { get; set; } = true;

    public bool AllowEscapeToExit { get; set; } = true;
}

public sealed class LaunchSettings
{
    public bool Enabled { get; set; } = true;

    public string? File { get; set; }

    public List<string> Candidates { get; set; } = ["start.bat", "启动.bat"];

    public string Arguments { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = ".";

    public bool WaitForExit { get; set; }

    public int PostLaunchDelayMs { get; set; } = 13000;

    public bool CloseWhenTargetMissing { get; set; }
}

public sealed class LoggingSettings
{
    public bool Enabled { get; set; } = true;

    public string File { get; set; } = "Logs/alls-launcher.log";
}
