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
        settings.Startup ??= new StartupSettings();
        settings.Input ??= new InputSettings();
        settings.Logging ??= new LoggingSettings();
        settings.Timeline ??= LauncherSettings.CreateDefaultTimeline();
        settings.Games ??= [];
        settings.Operations ??= [];
        settings.UpdateSources ??= [];
        settings.Input.Devices ??= [];

        settings.PlatformName = string.IsNullOrWhiteSpace(settings.PlatformName) ? "ALLS HX2.1" : settings.PlatformName;
        settings.Language = string.IsNullOrWhiteSpace(settings.Language) ? "zh-CN" : settings.Language;
        settings.LogoPath = string.IsNullOrWhiteSpace(settings.LogoPath) ? "Assets/logo.png" : settings.LogoPath;
        settings.LoadingPath = string.IsNullOrWhiteSpace(settings.LoadingPath)
            ? "Assets/logomode_load.gif"
            : settings.LoadingPath;
        settings.Startup.DefaultGameId ??= string.Empty;
        settings.Logging.File = string.IsNullOrWhiteSpace(settings.Logging.File)
            ? "Logs/alls-launcher.log"
            : settings.Logging.File;

        settings.Display.Width = Math.Clamp(settings.Display.Width, 640, 7680);
        settings.Display.Height = Math.Clamp(settings.Display.Height, 360, 4320);
        settings.Display.StepTransitionMs = Math.Clamp(settings.Display.StepTransitionMs, 0, 5_000);
        settings.Input.HotPlugIntervalMs = Math.Clamp(settings.Input.HotPlugIntervalMs, 250, 30_000);

        var usedUpdateSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < settings.UpdateSources.Count; index++)
        {
            var source = settings.UpdateSources[index];
            source.Id = EnsureUniqueId(source.Id, $"update-source-{index + 1}", usedUpdateSourceIds);
            source.BaseUrl ??= string.Empty;
            source.Username ??= string.Empty;
            source.Password ??= string.Empty;
            source.DriveLetter ??= string.Empty;
            source.Path ??= string.Empty;
            source.RequestTimeoutMs = Math.Clamp(source.RequestTimeoutMs, 1_000, 3_600_000);
        }

        if (settings.Timeline.Count == 0)
        {
            settings.Timeline = LauncherSettings.CreateDefaultTimeline();
        }

        ValidateTimeline(settings.Timeline);

        var usedGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < settings.Games.Count; index++)
        {
            var game = settings.Games[index];
            game.Id = EnsureUniqueId(game.Id, $"game-{index + 1}", usedGameIds);
            game.Title = string.IsNullOrWhiteSpace(game.Title) ? game.Id : game.Title;
            game.Description ??= string.Empty;
            game.ExitErrorTitle ??= "GAME PROGRAM ENDED";
            game.ExitErrorMessage ??= "游戏程序已经停止运行";
            game.Launch ??= new LaunchSettings();
            game.Monitor ??= new ProcessMonitorSettings();
            game.Update ??= new GameUpdateSettings();
            game.Monitor.ProcessNames ??= [];
            game.Monitor.Window ??= new WindowTargetSettings();
            game.Timeline ??= [];
            game.Update.SourceIds ??= [];
            game.Update.TargetDirectory = string.IsNullOrWhiteSpace(game.Update.TargetDirectory)
                ? "."
                : game.Update.TargetDirectory;
            game.Update.VersionFile = string.IsNullOrWhiteSpace(game.Update.VersionFile)
                ? "ABU_VERSION"
                : Path.GetFileName(game.Update.VersionFile);
            ValidateLaunch(game.Launch);
            ValidateTimeline(game.Timeline);
            game.Monitor.StartupTimeoutMs = Math.Clamp(game.Monitor.StartupTimeoutMs, 1000, 3_600_000);
            game.Monitor.PollIntervalMs = Math.Clamp(game.Monitor.PollIntervalMs, 50, 30_000);
            game.Monitor.ReadyDelayMs = Math.Clamp(game.Monitor.ReadyDelayMs, 0, 600_000);
        }

        var usedOperationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < settings.Operations.Count; index++)
        {
            var operation = settings.Operations[index];
            operation.Id = EnsureUniqueId(operation.Id, $"operation-{index + 1}", usedOperationIds);
            operation.Title = string.IsNullOrWhiteSpace(operation.Title) ? operation.Id : operation.Title;
            operation.Description ??= string.Empty;
            operation.Command ??= new LaunchSettings();
            operation.Confirmation ??= new ConfirmationSettings();
            operation.Confirmation.Title ??= string.Empty;
            operation.Confirmation.Message ??= string.Empty;
            ValidateLaunch(operation.Command);
        }

        foreach (var device in settings.Input.Devices)
        {
            device.Name ??= string.Empty;
            device.VendorId ??= string.Empty;
            device.ProductIds ??= [];
            device.Player = Math.Clamp(device.Player, 0, 2);
            device.DeviceIndex = Math.Clamp(device.DeviceIndex, 0, 16);
        }

        if (settings.Games.Count > 0
            && !settings.Games.Any(game => game.Id.Equals(settings.Startup.DefaultGameId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.Startup.DefaultGameId = settings.Games[0].Id;
        }

        return settings;
    }

    private static void ValidateLaunch(LaunchSettings launch)
    {
        launch.Candidates ??= [];
        launch.WorkingDirectory = string.IsNullOrWhiteSpace(launch.WorkingDirectory) ? "." : launch.WorkingDirectory;
        launch.Arguments ??= string.Empty;
    }

    private static void ValidateTimeline(IEnumerable<BootPhase> timeline)
    {
        foreach (var phase in timeline)
        {
            phase.Step = Math.Clamp(phase.Step, 0, 99);
            phase.DurationMs = Math.Clamp(phase.DurationMs, 0, 300_000);
            phase.MessageKey = string.IsNullOrWhiteSpace(phase.MessageKey)
                ? $"STEP_{phase.Step:00}_MESSAGE"
                : phase.MessageKey;
        }
    }

    private static string EnsureUniqueId(string? candidate, string fallback, ISet<string> used)
    {
        var baseId = string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();
        var id = baseId;
        var suffix = 2;
        while (!used.Add(id))
        {
            id = $"{baseId}-{suffix++}";
        }

        return id;
    }
}
