using System.Diagnostics;
using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal sealed class MachinePowerService(ILogService log)
{
    public bool Execute(OperationKind kind)
    {
        if (kind is not (OperationKind.Shutdown or OperationKind.Restart))
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            log.Error($"Machine power action is only supported on Windows: {kind}.");
            return false;
        }

        try
        {
            var arguments = kind == OperationKind.Shutdown ? "/s /t 0" : "/r /t 0";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                log.Error($"Windows did not start shutdown.exe for {kind}.");
                return false;
            }

            log.Info($"Machine power action requested: {kind}.");
            return true;
        }
        catch (Exception exception)
        {
            log.Error($"Machine power action failed: {kind}.", exception);
            return false;
        }
    }
}
