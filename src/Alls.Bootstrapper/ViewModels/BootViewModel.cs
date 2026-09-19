using System.IO;
using Alls.Bootstrapper.Models;
using Alls.Bootstrapper.Services;

namespace Alls.Bootstrapper.ViewModels;

internal sealed class BootViewModel : ViewModelBase, IDisposable
{
    private readonly LauncherSettings settings;
    private readonly LocalizationService localization;
    private readonly BootSequenceService sequence;
    private readonly ILogService log;
    private readonly CancellationTokenSource lifetime = new();
    private bool started;
    private string stepLabel = "STEP 01";
    private string message = string.Empty;
    private bool isError;
    private bool isBusy = true;

    public BootViewModel(
        LauncherSettings settings,
        LocalizationService localization,
        BootSequenceService sequence,
        ILogService log)
    {
        this.settings = settings;
        this.localization = localization;
        this.sequence = sequence;
        this.log = log;

        PlatformName = settings.PlatformName;
        LogoPath = ResolveAssetPath(settings.LogoPath);
        LoadingPath = ResolveAssetPath(settings.LoadingPath);
    }

    public event EventHandler? CloseRequested;

    public string PlatformName { get; }

    public string LogoPath { get; }

    public string LoadingPath { get; }

    public string StepLabel
    {
        get => stepLabel;
        private set => SetProperty(ref stepLabel, value);
    }

    public string Message
    {
        get => message;
        private set => SetProperty(ref message, value);
    }

    public bool IsError
    {
        get => isError;
        private set => SetProperty(ref isError, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public async Task StartAsync()
    {
        if (started)
        {
            return;
        }

        started = true;
        try
        {
            var result = await sequence.RunAsync(settings, ShowPhase, lifetime.Token);
            switch (result.Outcome)
            {
                case BootSequenceOutcome.TargetMissing:
                    ShowError("ERROR 9001", localization.Get("LAUNCH_TARGET_NOT_FOUND"));
                    if (settings.Launch.CloseWhenTargetMissing)
                    {
                        CloseRequested?.Invoke(this, EventArgs.Empty);
                    }
                    break;

                case BootSequenceOutcome.LaunchFailed:
                    ShowError("ERROR 9002", localization.Get("LAUNCH_TARGET_FAILED"));
                    break;

                case BootSequenceOutcome.PreviewCompleted:
                    IsBusy = false;
                    log.Info("Preview sequence completed.");
                    break;

                case BootSequenceOutcome.Completed:
                case BootSequenceOutcome.ExitRequested:
                    IsBusy = false;
                    if (settings.AutoCloseAfterSequence)
                    {
                        CloseRequested?.Invoke(this, EventArgs.Empty);
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            log.Info("Boot sequence cancelled.");
        }
        catch (Exception exception)
        {
            log.Error("Unexpected boot-sequence failure.", exception);
            ShowError("ERROR 9999", localization.Get("UNEXPECTED_ERROR"));
        }
    }

    private void ShowPhase(BootPhase phase)
    {
        IsError = false;
        IsBusy = true;
        StepLabel = $"STEP {phase.Step:00}";
        Message = localization.Get(phase.MessageKey);
    }

    private void ShowError(string step, string errorMessage)
    {
        IsError = true;
        IsBusy = false;
        StepLabel = step;
        Message = errorMessage;
    }

    private static string ResolveAssetPath(string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, AppContext.BaseDirectory);
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
