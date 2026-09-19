using System.IO;
using Alls.Bootstrapper.Models;
using Alls.Bootstrapper.Services;

namespace Alls.Bootstrapper.ViewModels;

internal enum LauncherScreen
{
    Boot,
    Menu,
    Error
}

internal sealed class WindowVisibilityEventArgs(bool visible) : EventArgs
{
    public bool Visible { get; } = visible;
}

internal sealed class MenuEntryViewModel
{
    private MenuEntryViewModel(string title, string description, GameSettings? game, OperationSettings? operation)
    {
        Title = title;
        Description = description;
        Game = game;
        Operation = operation;
    }

    public string Title { get; }

    public string Description { get; }

    public GameSettings? Game { get; }

    public OperationSettings? Operation { get; }

    public static MenuEntryViewModel ForGame(GameSettings game) =>
        new(game.Title, game.Description, game, null);

    public static MenuEntryViewModel ForOperation(OperationSettings operation) =>
        new(operation.Title, operation.Description, null, operation);
}

internal sealed class LauncherViewModel : ViewModelBase, IDisposable
{
    private readonly LauncherSettings settings;
    private readonly LocalizationService localization;
    private readonly BootSequenceService sequence;
    private readonly ProcessLauncher launcher;
    private readonly TargetMonitorService monitor;
    private readonly IInputService input;
    private readonly ILogService log;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? session;
    private BootSequenceControl? bootControl;
    private LauncherScreen screen = LauncherScreen.Boot;
    private string stepLabel = "STEP 01";
    private string message = string.Empty;
    private string errorTitle = string.Empty;
    private string errorMessage = string.Empty;
    private int selectedIndex;
    private bool isBusy = true;
    private bool bootSequenceActive;
    private bool started;
    private int sessionGeneration;

    public LauncherViewModel(
        LauncherSettings settings,
        LocalizationService localization,
        BootSequenceService sequence,
        ProcessLauncher launcher,
        TargetMonitorService monitor,
        IInputService input,
        ILogService log)
    {
        this.settings = settings;
        this.localization = localization;
        this.sequence = sequence;
        this.launcher = launcher;
        this.monitor = monitor;
        this.input = input;
        this.log = log;

        PlatformName = settings.PlatformName;
        LogoPath = ResolveAssetPath(settings.LogoPath);
        LoadingPath = ResolveAssetPath(settings.LoadingPath);
        MenuEntries = settings.Games.Select(MenuEntryViewModel.ForGame)
            .Concat(settings.Operations.Select(MenuEntryViewModel.ForOperation))
            .ToList();
    }

    public event EventHandler? CloseRequested;

    public event EventHandler<WindowVisibilityEventArgs>? WindowVisibilityRequested;

    public string PlatformName { get; }

    public string LogoPath { get; }

    public string LoadingPath { get; }

    public IReadOnlyList<MenuEntryViewModel> MenuEntries { get; }

    public string MenuTitle => localization.Get("OPERATION_MENU_TITLE");

    public string MenuHelp => localization.Get("OPERATION_MENU_HELP");

    public bool IsBootVisible => Screen == LauncherScreen.Boot;

    public bool IsMenuVisible => Screen == LauncherScreen.Menu;

    public bool IsErrorVisible => Screen == LauncherScreen.Error;

    public LauncherScreen Screen
    {
        get => screen;
        private set
        {
            if (!SetProperty(ref screen, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(IsBootVisible));
            RaisePropertyChanged(nameof(IsMenuVisible));
            RaisePropertyChanged(nameof(IsErrorVisible));
        }
    }

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

    public string ErrorTitle
    {
        get => errorTitle;
        private set => SetProperty(ref errorTitle, value);
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public int SelectedIndex
    {
        get => selectedIndex;
        private set => SetProperty(ref selectedIndex, value);
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
        if (settings.Startup.Mode == StartupMode.Menu)
        {
            ShowMenu();
            return;
        }

        var game = settings.Games.FirstOrDefault(game =>
            game.Id.Equals(settings.Startup.DefaultGameId, StringComparison.OrdinalIgnoreCase));
        if (game is null)
        {
            ShowError("ERROR 9000", localization.Get("NO_GAME_CONFIGURED"));
            return;
        }

        await RunGameAsync(game);
    }

    public void HandleInput(CabinetInputAction action)
    {
        switch (Screen)
        {
            case LauncherScreen.Boot when bootSequenceActive:
                if (action == CabinetInputAction.Select && settings.Startup.AllowSelectToOpenMenu)
                {
                    bootControl?.RequestMenu();
                }
                else if (action is CabinetInputAction.Down or CabinetInputAction.Confirm)
                {
                    bootControl?.RequestSkip();
                }

                break;

            case LauncherScreen.Menu:
                HandleMenuInput(action);
                break;

            case LauncherScreen.Error when action is CabinetInputAction.Select or CabinetInputAction.Confirm:
                ShowMenu();
                break;
        }
    }

    private void HandleMenuInput(CabinetInputAction action)
    {
        if (MenuEntries.Count == 0)
        {
            return;
        }

        switch (action)
        {
            case CabinetInputAction.Up:
                SelectedIndex = (SelectedIndex - 1 + MenuEntries.Count) % MenuEntries.Count;
                break;
            case CabinetInputAction.Down:
                SelectedIndex = (SelectedIndex + 1) % MenuEntries.Count;
                break;
            case CabinetInputAction.Confirm:
                _ = ExecuteMenuEntryAsync(MenuEntries[SelectedIndex]);
                break;
        }
    }

    private async Task ExecuteMenuEntryAsync(MenuEntryViewModel entry)
    {
        if (entry.Game is not null)
        {
            await RunGameAsync(entry.Game);
            return;
        }

        if (entry.Operation is null)
        {
            return;
        }

        if (entry.Operation.Kind == OperationKind.Exit)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var generation = BeginSession(out var token);
        Screen = LauncherScreen.Boot;
        StepLabel = "OPERATION";
        Message = entry.Operation.Title;
        IsBusy = true;
        var result = await launcher.LaunchAsync(entry.Operation.Command, token);
        if (!IsCurrent(generation))
        {
            return;
        }

        IsBusy = false;
        if (!result.Success)
        {
            ShowError("ERROR 9003", result.Error ?? localization.Get("LAUNCH_TARGET_FAILED"));
        }
        else if (entry.Operation.CloseAfterRun)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ShowMenuCore();
        }
    }

    private async Task RunGameAsync(GameSettings game)
    {
        var generation = BeginSession(out var token);
        input.Resume();
        WindowVisibilityRequested?.Invoke(this, new WindowVisibilityEventArgs(true));
        Screen = LauncherScreen.Boot;
        IsBusy = true;
        bootSequenceActive = true;
        bootControl = new BootSequenceControl();

        try
        {
            var timeline = game.Timeline.Count > 0 ? game.Timeline : settings.Timeline;
            var result = await sequence.RunAsync(
                timeline,
                game.Launch,
                ShowPhase,
                bootControl,
                input.Suspend,
                token);
            if (!IsCurrent(generation))
            {
                return;
            }

            bootSequenceActive = false;
            switch (result.Outcome)
            {
                case BootSequenceOutcome.MenuRequested:
                    ShowMenuCore();
                    return;
                case BootSequenceOutcome.TargetMissing:
                    ShowError("ERROR 9001", result.Error ?? localization.Get("LAUNCH_TARGET_NOT_FOUND"));
                    return;
                case BootSequenceOutcome.LaunchFailed:
                    ShowError("ERROR 9002", result.Error ?? localization.Get("LAUNCH_TARGET_FAILED"));
                    return;
                case BootSequenceOutcome.ExitRequested:
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    return;
                case BootSequenceOutcome.PreviewCompleted:
                case BootSequenceOutcome.Completed:
                    IsBusy = false;
                    log.Info("Preview/boot sequence completed without a monitored game launch.");
                    return;
                case BootSequenceOutcome.Launched:
                    break;
            }

            StepLabel = "GAME";
            Message = localization.Get("WAITING_FOR_GAME");
            IsBusy = true;
            var ready = await monitor.WaitUntilReadyAsync(game.Monitor, token);
            if (!IsCurrent(generation))
            {
                return;
            }

            if (!ready.Ready)
            {
                ShowError("ERROR 9004", ready.Error ?? localization.Get("GAME_START_TIMEOUT"));
                return;
            }

            IsBusy = false;
            WindowVisibilityRequested?.Invoke(this, new WindowVisibilityEventArgs(false));
            await monitor.WaitUntilStoppedAsync(game.Monitor, token);
            if (!IsCurrent(generation))
            {
                return;
            }

            WindowVisibilityRequested?.Invoke(this, new WindowVisibilityEventArgs(true));
            input.Resume();
            if (game.OnExit == GameExitBehavior.Error)
            {
                ShowError(game.ExitErrorTitle, game.ExitErrorMessage);
            }
            else
            {
                ShowMenuCore();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            log.Info($"Game session cancelled: {game.Id}.");
        }
        catch (Exception exception)
        {
            log.Error($"Unexpected game-session failure: {game.Id}.", exception);
            if (IsCurrent(generation))
            {
                WindowVisibilityRequested?.Invoke(this, new WindowVisibilityEventArgs(true));
                input.Resume();
                ShowError("ERROR 9999", localization.Get("UNEXPECTED_ERROR"));
            }
        }
        finally
        {
            if (IsCurrent(generation))
            {
                bootSequenceActive = false;
            }
        }
    }

    private void ShowPhase(BootPhase phase)
    {
        Screen = LauncherScreen.Boot;
        IsBusy = true;
        StepLabel = $"STEP {phase.Step:00}";
        Message = localization.Get(phase.MessageKey);
    }

    private void ShowMenu()
    {
        BeginSession(out _);
        input.Resume();
        WindowVisibilityRequested?.Invoke(this, new WindowVisibilityEventArgs(true));
        ShowMenuCore();
    }

    private void ShowMenuCore()
    {
        input.Resume();
        bootSequenceActive = false;
        IsBusy = false;
        Screen = LauncherScreen.Menu;
        if (SelectedIndex >= MenuEntries.Count)
        {
            SelectedIndex = 0;
        }
    }

    private void ShowError(string title, string errorMessage)
    {
        input.Resume();
        bootSequenceActive = false;
        IsBusy = false;
        ErrorTitle = title;
        ErrorMessage = errorMessage;
        Screen = LauncherScreen.Error;
    }

    private int BeginSession(out CancellationToken cancellationToken)
    {
        session?.Cancel();
        session?.Dispose();
        session = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellationToken = session.Token;
        return ++sessionGeneration;
    }

    private bool IsCurrent(int generation) => generation == sessionGeneration && !lifetime.IsCancellationRequested;

    private static string ResolveAssetPath(string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, AppContext.BaseDirectory);

    public void Dispose()
    {
        lifetime.Cancel();
        session?.Cancel();
        session?.Dispose();
        lifetime.Dispose();
    }
}
