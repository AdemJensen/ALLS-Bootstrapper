using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alls.Bootstrapper.Models;

namespace Alls.Configurator.Services;

internal sealed class ConfigurationDocumentService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public LauncherSettings Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var settings = JsonSerializer.Deserialize<LauncherSettings>(json, ReadOptions)
            ?? throw new InvalidDataException("配置文件内容为空。");
        Normalize(settings);
        return settings;
    }

    public LauncherSettings Clone(LauncherSettings settings) =>
        JsonSerializer.Deserialize<LauncherSettings>(Serialize(settings), ReadOptions)
        ?? throw new InvalidOperationException("无法复制配置对象。");

    public string Serialize(LauncherSettings settings) => JsonSerializer.Serialize(settings, WriteOptions) + Environment.NewLine;

    public void Save(string path, LauncherSettings settings)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("无法确定配置文件所在目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, Serialize(settings), new UTF8Encoding(false));
        try
        {
            if (File.Exists(fullPath))
            {
                File.Copy(fullPath, fullPath + ".bak", true);
            }

            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static void Normalize(LauncherSettings settings)
    {
        settings.Display ??= new DisplaySettings();
        settings.Startup ??= new StartupSettings();
        settings.Input ??= new InputSettings();
        settings.Logging ??= new LoggingSettings();
        settings.Timeline ??= [];
        settings.Games ??= [];
        settings.Operations ??= [];
        settings.UpdateSources ??= [];
        settings.Input.Devices ??= [];
        settings.PlatformName ??= string.Empty;
        settings.Language ??= string.Empty;
        settings.LogoPath ??= string.Empty;
        settings.LoadingPath ??= string.Empty;
        settings.Startup.DefaultGameId ??= string.Empty;
        settings.Logging.File ??= string.Empty;

        foreach (var source in settings.UpdateSources)
        {
            source.Id ??= string.Empty;
            source.BaseUrl ??= string.Empty;
            source.Username ??= string.Empty;
            source.Password ??= string.Empty;
            source.DriveLetter ??= string.Empty;
            source.Path ??= string.Empty;
        }

        foreach (var game in settings.Games)
        {
            game.Id ??= string.Empty;
            game.Title ??= string.Empty;
            game.Description ??= string.Empty;
            game.Language ??= string.Empty;
            game.Launch ??= new LaunchSettings();
            game.Monitor ??= new ProcessMonitorSettings();
            game.Monitor.ProcessNames ??= [];
            game.Monitor.Window ??= new WindowTargetSettings();
            game.Update ??= new GameUpdateSettings();
            game.Update.SourceIds ??= [];
            game.Timeline ??= [];
            NormalizeLaunch(game.Launch);
        }

        foreach (var operation in settings.Operations)
        {
            operation.Id ??= string.Empty;
            operation.Title ??= string.Empty;
            operation.Description ??= string.Empty;
            operation.Command ??= new LaunchSettings();
            operation.Confirmation ??= new ConfirmationSettings();
            operation.Confirmation.Title ??= string.Empty;
            operation.Confirmation.Message ??= string.Empty;
            NormalizeLaunch(operation.Command);
        }

        foreach (var device in settings.Input.Devices)
        {
            device.Name ??= string.Empty;
            device.VendorId ??= string.Empty;
            device.ProductIds ??= [];
        }
    }

    private static void NormalizeLaunch(LaunchSettings launch)
    {
        launch.Candidates ??= [];
        launch.Arguments ??= string.Empty;
        launch.WorkingDirectory ??= ".";
    }
}
