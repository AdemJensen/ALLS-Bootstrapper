using System.IO;
using System.Windows;

namespace Alls.Configurator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var requestedPath = e.Args.FirstOrDefault(argument => !argument.StartsWith('-'));
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            var adjacentConfiguration = Path.Combine(AppContext.BaseDirectory, "alls-launcher.json");
            if (File.Exists(adjacentConfiguration)) requestedPath = adjacentConfiguration;
        }
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            window.OpenFromCommandLine(requestedPath);
        }
    }
}
