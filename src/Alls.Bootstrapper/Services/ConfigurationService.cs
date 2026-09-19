using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal sealed class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public LauncherSettings Load(string? requestedPath)
    {
        var path = ResolvePath(requestedPath);
        if (!File.Exists(path))
        {
            return Validate(new LauncherSettings());
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<LauncherSettings>(json, JsonOptions)
            ?? throw new InvalidDataException($"Configuration is empty: {path}");

        return Validate(settings);
    }

    private static string ResolvePath(string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return Path.Combine(AppContext.BaseDirectory, "alls-launcher.json");
        }

        return Path.GetFullPath(requestedPath, Environment.CurrentDirectory);
    }

    private static LauncherSettings Validate(LauncherSettings settings)
    {
        settings.Display ??= new DisplaySettings();
        settings.Launch ??= new LaunchSettings();
        settings.Logging ??= new LoggingSettings();
        settings.Timeline ??= LauncherSettings.CreateDefaultTimeline();
        settings.Launch.Candidates ??= [];

        settings.PlatformName = string.IsNullOrWhiteSpace(settings.PlatformName) ? "ALLS HX2.1" : settings.PlatformName;
        settings.Language = string.IsNullOrWhiteSpace(settings.Language) ? "zh-CN" : settings.Language;
        settings.LogoPath = string.IsNullOrWhiteSpace(settings.LogoPath) ? "Assets/logo.png" : settings.LogoPath;
        settings.LoadingPath = string.IsNullOrWhiteSpace(settings.LoadingPath)
            ? "Assets/logomode_load.gif"
            : settings.LoadingPath;
        settings.Launch.WorkingDirectory = string.IsNullOrWhiteSpace(settings.Launch.WorkingDirectory)
            ? "."
            : settings.Launch.WorkingDirectory;
        settings.Launch.Arguments ??= string.Empty;
        settings.Logging.File = string.IsNullOrWhiteSpace(settings.Logging.File)
            ? "Logs/alls-launcher.log"
            : settings.Logging.File;

        settings.Display.Width = Math.Clamp(settings.Display.Width, 640, 7680);
        settings.Display.Height = Math.Clamp(settings.Display.Height, 360, 4320);
        settings.Launch.PostLaunchDelayMs = Math.Clamp(settings.Launch.PostLaunchDelayMs, 0, 300_000);

        if (settings.Timeline.Count == 0)
        {
            settings.Timeline = LauncherSettings.CreateDefaultTimeline();
        }

        foreach (var phase in settings.Timeline)
        {
            phase.Step = Math.Clamp(phase.Step, 0, 99);
            phase.DurationMs = Math.Clamp(phase.DurationMs, 0, 300_000);
            phase.MessageKey = string.IsNullOrWhiteSpace(phase.MessageKey)
                ? $"STEP_{phase.Step:00}_MESSAGE"
                : phase.MessageKey;
        }

        return settings;
    }
}
