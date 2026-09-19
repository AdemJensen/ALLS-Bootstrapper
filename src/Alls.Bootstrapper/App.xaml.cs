using System.Windows;
using Alls.Bootstrapper.Infrastructure;
using Alls.Bootstrapper.Services;
using Alls.Bootstrapper.ViewModels;

namespace Alls.Bootstrapper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var options = CommandLineOptions.Parse(e.Args);
            var configuration = new ConfigurationService();
            var settings = configuration.Load(options.ConfigPath);

            options.ApplyTo(settings);

            var localization = new LocalizationService();
            localization.Configure(settings.Language);

            var logger = new FileLogService(settings.Logging);
            logger.Info("ALLS bootstrapper starting.");

            var launcher = new ProcessLauncher(logger);
            var sequence = new BootSequenceService(launcher, logger);
            var monitor = new TargetMonitorService(logger);
            var input = new MaimaiHidInputService(settings.Input, logger);
            var viewModel = new LauncherViewModel(
                settings,
                localization,
                sequence,
                launcher,
                monitor,
                input,
                logger);
            var window = new MainWindow(settings.Display, settings.Input, viewModel, input, logger);

            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"ALLS Bootstrapper could not start.\n\n{exception.Message}",
                "ALLS Bootstrapper",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
