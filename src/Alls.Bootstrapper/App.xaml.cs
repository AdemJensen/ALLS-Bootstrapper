using System.Windows;
using Alls.Bootstrapper.Infrastructure;
using Alls.Bootstrapper.Services;
using Alls.Bootstrapper.ViewModels;

namespace Alls.Bootstrapper;

public partial class App : Application
{
    private ILogService? log;

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
            log = logger;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            logger.Info("ALLS bootstrapper starting.");

            var launcher = new ProcessLauncher(logger);
            var sequence = new BootSequenceService(launcher, logger);
            var monitor = new TargetMonitorService(logger);
            var updater = new GameUpdateService(logger);
            var machinePower = new MachinePowerService(logger);
            var input = new MaimaiHidInputService(settings.Input, logger);
            var viewModel = new LauncherViewModel(
                settings,
                localization,
                sequence,
                launcher,
                monitor,
                updater,
                machinePower,
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

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        log?.Error("Unhandled UI exception.", e.Exception);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        log?.Error(
            $"Unhandled application exception. Terminating: {e.IsTerminating}.",
            e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        log?.Error("Unobserved task exception.", e.Exception);
    }
}
