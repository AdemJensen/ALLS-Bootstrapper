using System.IO;
using System.Text;
using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal sealed class FileLogService : ILogService
{
    private readonly object gate = new();
    private readonly bool enabled;
    private readonly string path;

    public FileLogService(LoggingSettings settings)
    {
        enabled = settings.Enabled;
        path = Path.GetFullPath(settings.File, AppContext.BaseDirectory);
    }

    public void Info(string message) => Write("INF", message);

    public void Error(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message} {exception}";
        Write("ERR", detail);
    }

    private void Write(string level, string message)
    {
        if (!enabled)
        {
            return;
        }

        try
        {
            lock (gate)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never prevent the cabinet from booting.
        }
    }
}
