using System.Diagnostics;
using System.IO;
using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal sealed record LaunchResult(bool Success, bool Skipped, string? Target, string? Error)
{
    public static LaunchResult Preview() => new(true, true, null, null);

    public static LaunchResult Missing(string message) => new(false, false, null, message);
}

internal sealed class ProcessLauncher(ILogService log)
{
    public async Task<LaunchResult> LaunchAsync(LaunchSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            log.Info("Launch skipped because preview mode is active.");
            return LaunchResult.Preview();
        }

        var workingDirectory = Path.GetFullPath(settings.WorkingDirectory, AppContext.BaseDirectory);
        var target = FindTarget(settings, workingDirectory);
        if (target is null)
        {
            var message = $"No launch target was found in '{workingDirectory}'.";
            log.Error(message);
            return LaunchResult.Missing(message);
        }

        try
        {
            var startInfo = CreateStartInfo(target, settings.Arguments, workingDirectory);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return LaunchResult.Missing($"Windows did not start '{target}'.");
            }

            log.Info($"Started launch target: {target}");
            if (settings.WaitForExit)
            {
                await process.WaitForExitAsync(cancellationToken);
                log.Info($"Launch target exited with code {process.ExitCode}.");
            }

            return new LaunchResult(true, false, target, null);
        }
        catch (Exception exception)
        {
            log.Error($"Failed to start '{target}'.", exception);
            return new LaunchResult(false, false, target, exception.Message);
        }
    }

    private static string? FindTarget(LaunchSettings settings, string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(settings.File))
        {
            var explicitPath = ResolveCandidate(settings.File, workingDirectory);
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        foreach (var candidate in settings.Candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            var candidatePath = ResolveCandidate(candidate, workingDirectory);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static string ResolveCandidate(string candidate, string workingDirectory)
    {
        return Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(candidate, workingDirectory);
    }

    private static ProcessStartInfo CreateStartInfo(string target, string arguments, string workingDirectory)
    {
        var extension = Path.GetExtension(target);
        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var command = string.IsNullOrWhiteSpace(arguments)
                ? $"\"{target}\""
                : $"\"{target}\" {arguments}";

            return new ProcessStartInfo
            {
                FileName = commandInterpreter,
                Arguments = $"/d /s /c \"{command}\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = target,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };
    }
}
