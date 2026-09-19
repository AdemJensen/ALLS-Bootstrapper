using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal sealed record TargetReadyResult(bool Ready, string? Error = null);

internal sealed class TargetMonitorService(ILogService log)
{
    private const int SwShow = 5;
    private const int SwRestore = 9;

    public async Task<TargetReadyResult> WaitUntilReadyAsync(
        ProcessMonitorSettings settings,
        CancellationToken cancellationToken)
    {
        if (!HasCriteria(settings))
        {
            log.Info("No process/window monitor is configured; target is considered ready immediately.");
            return new TargetReadyResult(true);
        }

        var startedAt = Stopwatch.StartNew();
        while (startedAt.ElapsedMilliseconds < settings.StartupTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = Capture(settings);
            if (IsReady(state, settings.ReadyMode))
            {
                log.Info($"Game target became ready ({state}).");
                return new TargetReadyResult(true);
            }

            await Task.Delay(settings.PollIntervalMs, cancellationToken);
        }

        var error = $"Timed out after {settings.StartupTimeoutMs} ms while waiting for the configured process/window.";
        log.Error(error);
        return new TargetReadyResult(false, error);
    }

    public async Task WaitUntilStoppedAsync(ProcessMonitorSettings settings, CancellationToken cancellationToken)
    {
        if (!HasCriteria(settings))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = Capture(settings);
            if (IsLost(state, settings.ExitMode))
            {
                log.Info($"Game target disappeared ({state}).");
                return;
            }

            await Task.Delay(settings.PollIntervalMs, cancellationToken);
        }
    }

    public bool TryActivateTargetWindow(ProcessMonitorSettings settings, bool hideCursor)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = FindTargetWindow(settings);
        if (handle == IntPtr.Zero)
        {
            log.Info("No visible game window was available for foreground focus handoff.");
            return false;
        }

        _ = ShowWindowAsync(handle, IsIconic(handle) ? SwRestore : SwShow);
        _ = BringWindowToTop(handle);
        var activated = SetForegroundWindow(handle);
        if (hideCursor)
        {
            _ = SetCursor(IntPtr.Zero);
        }

        log.Info(activated
            ? $"Foreground focus handed to game window 0x{handle.ToInt64():X}."
            : $"Windows rejected foreground focus handoff to game window 0x{handle.ToInt64():X}.");
        return activated;
    }

    private static bool HasCriteria(ProcessMonitorSettings settings) =>
        settings.ProcessNames.Any(name => !string.IsNullOrWhiteSpace(name)) || settings.Window.Enabled;

    private static TargetState Capture(ProcessMonitorSettings settings)
    {
        var hasProcessCriterion = settings.ProcessNames.Any(name => !string.IsNullOrWhiteSpace(name));
        var processPresent = !hasProcessCriterion || settings.ProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Any(IsProcessRunning);

        var hasWindowCriterion = settings.Window.Enabled;
        var windowPresent = !hasWindowCriterion || FindMatchingWindow(settings.Window) != IntPtr.Zero;
        return new TargetState(hasProcessCriterion, processPresent, hasWindowCriterion, windowPresent);
    }

    private static bool IsReady(TargetState state, TargetMatchMode mode)
    {
        var checks = state.Checks;
        return mode == TargetMatchMode.All ? checks.All(value => value) : checks.Any(value => value);
    }

    private static bool IsLost(TargetState state, TargetLossMode mode)
    {
        var missing = state.Checks.Select(value => !value);
        return mode == TargetLossMode.AllMissing ? missing.All(value => value) : missing.Any(value => value);
    }

    private static bool IsProcessRunning(string configuredName)
    {
        var processName = Path.GetFileNameWithoutExtension(configuredName.Trim());
        try
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr FindTargetWindow(ProcessMonitorSettings settings)
    {
        if (settings.Window.Enabled)
        {
            var configuredWindow = FindMatchingWindow(settings.Window);
            if (configuredWindow != IntPtr.Zero)
            {
                return configuredWindow;
            }
        }

        var processNames = settings.ProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Path.GetFileNameWithoutExtension(name.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (processNames.Count == 0)
        {
            return IntPtr.Zero;
        }

        return FindWindow(handle =>
        {
            GetWindowThreadProcessId(handle, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                return processNames.Contains(process.ProcessName);
            }
            catch
            {
                return false;
            }
        });
    }

    private static IntPtr FindMatchingWindow(WindowTargetSettings target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }

        return FindWindow(handle =>
        {
            GetWindowThreadProcessId(handle, out var processId);
            return MatchesProcess(processId, target.ProcessName)
                && Contains(GetWindowTitle(handle), target.TitleContains)
                && EqualsIfConfigured(GetWindowClass(handle), target.ClassName);
        });
    }

    private static IntPtr FindWindow(Func<IntPtr, bool> predicate)
    {
        var found = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || !predicate(handle))
            {
                return true;
            }

            found = handle;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool MatchesProcess(uint processId, string configuredName)
    {
        if (string.IsNullOrWhiteSpace(configuredName))
        {
            return true;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals(
                Path.GetFileNameWithoutExtension(configuredName.Trim()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool Contains(string actual, string expected) =>
        string.IsNullOrWhiteSpace(expected)
        || actual.Contains(expected, StringComparison.OrdinalIgnoreCase);

    private static bool EqualsIfConfigured(string actual, string expected) =>
        string.IsNullOrWhiteSpace(expected)
        || actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetWindowClass(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        _ = GetClassName(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private sealed record TargetState(
        bool HasProcessCriterion,
        bool ProcessPresent,
        bool HasWindowCriterion,
        bool WindowPresent)
    {
        public IEnumerable<bool> Checks
        {
            get
            {
                if (HasProcessCriterion)
                {
                    yield return ProcessPresent;
                }

                if (HasWindowCriterion)
                {
                    yield return WindowPresent;
                }
            }
        }

        public override string ToString() => $"process={ProcessPresent}, window={WindowPresent}";
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);
}
