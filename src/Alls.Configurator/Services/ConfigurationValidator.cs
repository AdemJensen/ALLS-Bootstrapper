using System.Text.RegularExpressions;
using Alls.Bootstrapper.Models;

namespace Alls.Configurator.Services;

internal enum ValidationSeverity { Error, Warning }

internal sealed record ValidationIssue(ValidationSeverity Severity, string Section, string Message)
{
    public string Icon => Severity == ValidationSeverity.Error ? "错误" : "警告";
}

internal static partial class ConfigurationValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(LauncherSettings settings)
    {
        var issues = new List<ValidationIssue>();

        Required(issues, "基础设置", settings.PlatformName, "平台名称不能为空");
        Required(issues, "基础设置", settings.Language, "界面语言不能为空");
        Range(issues, "显示", settings.Display.Width, 640, 7680, "窗口宽度");
        Range(issues, "显示", settings.Display.Height, 360, 4320, "窗口高度");
        Range(issues, "显示", settings.Display.StepTransitionMs, 0, 5000, "STEP 淡入时间");
        Range(issues, "输入设备", settings.Input.HotPlugIntervalMs, 250, 30000, "热插拔扫描间隔");

        UniqueIds(issues, "游戏", settings.Games.Select(game => game.Id));
        UniqueIds(issues, "更新源", settings.UpdateSources.Select(source => source.Id));
        UniqueIds(issues, "机台操作", settings.Operations.Select(operation => operation.Id));

        var gameIds = settings.Games.Select(game => game.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (settings.Startup.Mode == StartupMode.DefaultGame && !gameIds.Contains(settings.Startup.DefaultGameId))
        {
            issues.Add(new(ValidationSeverity.Error, "启动", $"默认游戏“{settings.Startup.DefaultGameId}”不存在"));
        }

        ValidateTimeline(issues, "默认时间线", settings.Timeline);

        var sourceIds = settings.UpdateSources.Select(source => source.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var game in settings.Games)
        {
            var label = string.IsNullOrWhiteSpace(game.Title) ? game.Id : game.Title;
            Required(issues, "游戏", game.Id, $"游戏“{label}”缺少 ID");
            Required(issues, "游戏", game.Title, $"游戏“{game.Id}”缺少标题");
            ValidateLaunch(issues, $"游戏 / {label}", game.Launch);
            ValidateTimeline(issues, $"游戏时间线 / {label}", game.Timeline);
            Range(issues, $"游戏 / {label}", game.Monitor.StartupTimeoutMs, 1000, 3_600_000, "启动超时");
            Range(issues, $"游戏 / {label}", game.Monitor.PollIntervalMs, 50, 30_000, "监控轮询间隔");
            Range(issues, $"游戏 / {label}", game.Monitor.ReadyDelayMs, 0, 600_000, "就绪延迟");

            if (game.Launch.Enabled && string.IsNullOrWhiteSpace(game.Launch.File) && game.Launch.Candidates.Count == 0)
            {
                issues.Add(new(ValidationSeverity.Error, $"游戏 / {label}", "已启用启动命令，但文件和候选文件均为空"));
            }

            foreach (var missingId in game.Update.SourceIds
                         .Where(id => !sourceIds.Contains(id))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new(ValidationSeverity.Error, $"游戏 / {label}", $"更新源 ID 列表包含不存在的更新源“{missingId}”"));
            }

            if (game.Monitor.ProcessNames.Count == 0 && !game.Monitor.Window.Enabled)
            {
                issues.Add(new(ValidationSeverity.Warning, $"游戏 / {label}", "没有配置进程或窗口目标，启动器无法监控游戏状态"));
            }
        }

        foreach (var source in settings.UpdateSources)
        {
            var label = string.IsNullOrWhiteSpace(source.Id) ? "未命名更新源" : source.Id;
            Required(issues, "更新源", source.Id, "更新源 ID 不能为空");
            Range(issues, $"更新源 / {label}", source.RequestTimeoutMs, 1000, 3_600_000, "请求超时");
            if (source.Kind == UpdateSourceKind.Http && source.Enabled
                && (!Uri.TryCreate(source.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                issues.Add(new(ValidationSeverity.Error, $"更新源 / {label}", "HTTP 地址必须是有效的 http:// 或 https:// URL"));
            }
            if (source.Kind == UpdateSourceKind.Usb && source.Enabled && string.IsNullOrWhiteSpace(source.DriveLetter))
            {
                issues.Add(new(ValidationSeverity.Warning, $"更新源 / {label}", "U 盘盘符为空，将无法定位更新目录"));
            }
        }

        foreach (var device in settings.Input.Devices)
        {
            var label = string.IsNullOrWhiteSpace(device.Name) ? "未命名设备" : device.Name;
            if (device.Enabled && !HexIdRegex().IsMatch(device.VendorId))
            {
                issues.Add(new(ValidationSeverity.Error, $"输入设备 / {label}", "VID 应为 0x0000 格式的十六进制值"));
            }
            foreach (var productId in device.ProductIds.Where(id => !HexIdRegex().IsMatch(id)))
            {
                issues.Add(new(ValidationSeverity.Error, $"输入设备 / {label}", $"PID“{productId}”格式无效，应为 0x0000"));
            }
            Range(issues, $"输入设备 / {label}", device.Player, 0, 2, "玩家编号");
            Range(issues, $"输入设备 / {label}", device.DeviceIndex, 0, 16, "设备序号");
        }

        foreach (var operation in settings.Operations)
        {
            var label = string.IsNullOrWhiteSpace(operation.Title) ? operation.Id : operation.Title;
            Required(issues, "机台操作", operation.Id, $"操作“{label}”缺少 ID");
            Required(issues, "机台操作", operation.Title, $"操作“{operation.Id}”缺少标题");
            ValidateLaunch(issues, $"机台操作 / {label}", operation.Command);
            if (operation.Kind == OperationKind.Command && operation.Command.Enabled
                && string.IsNullOrWhiteSpace(operation.Command.File) && operation.Command.Candidates.Count == 0)
            {
                issues.Add(new(ValidationSeverity.Error, $"机台操作 / {label}", "自定义命令已启用，但文件和候选文件均为空"));
            }
        }

        if (settings.Games.Count == 0)
        {
            issues.Add(new(ValidationSeverity.Warning, "游戏", "没有配置任何游戏"));
        }

        return issues;
    }

    private static void ValidateLaunch(List<ValidationIssue> issues, string section, LaunchSettings launch)
    {
        if (launch.Enabled && string.IsNullOrWhiteSpace(launch.WorkingDirectory))
        {
            issues.Add(new(ValidationSeverity.Error, section, "工作目录不能为空"));
        }
    }

    private static void ValidateTimeline(List<ValidationIssue> issues, string section, IEnumerable<BootPhase> timeline)
    {
        foreach (var phase in timeline)
        {
            Range(issues, section, phase.Step, 0, 99, "STEP 编号");
            Range(issues, section, phase.DurationMs, 0, 300_000, "持续时间");
            Required(issues, section, phase.MessageKey, $"STEP {phase.Step:00} 的消息键不能为空");
        }
    }

    private static void UniqueIds(List<ValidationIssue> issues, string section, IEnumerable<string> ids)
    {
        foreach (var group in ids.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            issues.Add(new(ValidationSeverity.Error, section, $"ID“{group.Key}”重复"));
        }
    }

    private static void Required(List<ValidationIssue> issues, string section, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) issues.Add(new(ValidationSeverity.Error, section, message));
    }

    private static void Range(List<ValidationIssue> issues, string section, int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            issues.Add(new(ValidationSeverity.Error, section, $"{name}必须在 {minimum}–{maximum} 之间（当前为 {value}）"));
        }
    }

    [GeneratedRegex("^(?:0x)?[0-9a-fA-F]{4}$")]
    private static partial Regex HexIdRegex();
}
