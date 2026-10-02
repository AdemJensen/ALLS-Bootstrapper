using System.IO;
using System.Text;
using System.Windows.Media;
using Alls.Bootstrapper.Models;
using Alls.Bootstrapper.Services;

namespace Alls.Bootstrapper.ViewModels;

internal enum LauncherScreen
{
    Boot,
    Menu,
    Confirmation,
    UpdateAll,
    Error
}

internal enum MenuSection
{
    Games,
    Operations
}

internal enum UpdateAllSection
{
    Games,
    Return
}

internal enum UpdateStatusTone
{
    Neutral,
    Notice,
    InProgress,
    Success,
    Failure
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

internal sealed class GameUpdateItemViewModel(
    GameSettings game,
    string status,
    UpdateStatusTone tone) : ViewModelBase
{
    private string status = status;
    private string details = string.Empty;
    private Brush statusForeground = GetStatusBrush(tone);
    private string progressText = string.Empty;
    private double progressPercentage;
    private bool isProgressVisible;
    private bool isProgressIndeterminate;
    private bool hasFileProgress;
    private bool terminal;
    private int totalFiles;
    private int copiedFiles;

    public GameSettings Game { get; } = game;

    public string Title => Game.Title;

    public string Id => Game.Id;

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string Details
    {
        get => details;
        private set => SetProperty(ref details, value);
    }

    public Brush StatusForeground
    {
        get => statusForeground;
        private set => SetProperty(ref statusForeground, value);
    }

    public string ProgressText
    {
        get => progressText;
        private set => SetProperty(ref progressText, value);
    }

    public double ProgressPercentage
    {
        get => progressPercentage;
        private set => SetProperty(ref progressPercentage, value);
    }

    public bool IsProgressVisible
    {
        get => isProgressVisible;
        private set => SetProperty(ref isProgressVisible, value);
    }

    public bool IsProgressIndeterminate
    {
        get => isProgressIndeterminate;
        private set => SetProperty(ref isProgressIndeterminate, value);
    }

    public bool HasCopyStatistics => hasFileProgress;

    public int TotalFiles => totalFiles;

    public int CopiedFiles => copiedFiles;

    public void ApplyProgress(
        string newStatus,
        string newProgressText,
        int completedFiles,
        int totalFiles,
        bool indeterminate,
        bool isCopying)
    {
        if (terminal)
        {
            return;
        }

        Status = newStatus;
        StatusForeground = GetStatusBrush(UpdateStatusTone.InProgress);
        ProgressText = newProgressText;
        IsProgressVisible = true;
        IsProgressIndeterminate = indeterminate;
        if (indeterminate)
        {
            ProgressPercentage = 0;
        }

        if (!indeterminate)
        {
            if (isCopying)
            {
                hasFileProgress = true;
                this.totalFiles = totalFiles;
                copiedFiles = completedFiles;
            }

            ProgressPercentage = totalFiles == 0
                ? 100
                : Math.Clamp(completedFiles * 100.0 / totalFiles, 0, 100);
        }
    }

    public void Apply(
        string newStatus,
        string newDetails,
        UpdateStatusTone newTone,
        int resultTotalFiles = 0,
        int resultCopiedFiles = 0,
        bool hasCopyStatistics = false)
    {
        terminal = true;
        Status = newStatus;
        Details = newDetails;
        StatusForeground = GetStatusBrush(newTone);
        if (hasCopyStatistics)
        {
            hasFileProgress = true;
            totalFiles = resultTotalFiles;
            copiedFiles = resultCopiedFiles;
        }

        IsProgressVisible = false;
        IsProgressIndeterminate = false;
    }

    private static Brush GetStatusBrush(UpdateStatusTone tone) => tone switch
    {
        UpdateStatusTone.Success => Brushes.ForestGreen,
        UpdateStatusTone.Failure => Brushes.Crimson,
        UpdateStatusTone.InProgress => Brushes.DodgerBlue,
        UpdateStatusTone.Notice => Brushes.DarkGoldenrod,
        _ => Brushes.Gray
    };
}

internal sealed class LauncherViewModel : ViewModelBase, IDisposable
{
    private readonly LauncherSettings settings;
    private readonly LocalizationService localization;
    private readonly BootSequenceService sequence;
    private readonly ProcessLauncher launcher;
    private readonly TargetMonitorService monitor;
    private readonly GameUpdateService updater;
    private readonly MachinePowerService machinePower;
    private readonly IInputService input;
    private readonly ILogService log;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? session;
    private BootSequenceControl? bootControl;
    private LauncherScreen screen = LauncherScreen.Boot;
    private string stepLabel = "STEP 01";
    private string message = string.Empty;
    private string bootUpdateProgressText = string.Empty;
    private string errorTitle = string.Empty;
    private string errorMessage = string.Empty;
    private int selectedGameIndex;
    private int selectedOperationIndex;
    private int confirmationSelectedIndex;
    private MenuSection menuSection = MenuSection.Games;
    private OperationSettings? pendingOperation;
    private string confirmationTitle = string.Empty;
    private string confirmationMessage = string.Empty;
    private string activeGameLanguage = string.Empty;
    private IReadOnlyList<GameUpdateItemViewModel> updateAllEntries = [];
    private int selectedUpdateGameIndex;
    private UpdateAllSection updateAllSection = UpdateAllSection.Return;
    private bool updateAllRunning;
    private UpdatePauseController? updatePauseController;
    private bool isUpdateDetailsVisible;
    private bool isUpdateCancelConfirmationVisible;
    private int updateCancelConfirmationSelectedIndex;
    private string updateAllTitle = string.Empty;
    private string updateDetailTitle = string.Empty;
    private string updateDetailMessage = string.Empty;
    private bool isBusy = true;
    private bool bootSequenceActive;
    private bool started;
    private int sessionGeneration;
    private DisplayLayoutMode activeLayoutMode;

    public LauncherViewModel(
        LauncherSettings settings,
        LocalizationService localization,
        BootSequenceService sequence,
        ProcessLauncher launcher,
        TargetMonitorService monitor,
        GameUpdateService updater,
        MachinePowerService machinePower,
        IInputService input,
        ILogService log)
    {
        this.settings = settings;
        this.localization = localization;
        this.sequence = sequence;
        this.launcher = launcher;
        this.monitor = monitor;
        this.updater = updater;
        this.machinePower = machinePower;
        this.input = input;
        this.log = log;
        activeLayoutMode = settings.Display.LayoutMode;

        PlatformName = settings.PlatformName;
        LogoPath = ResolveAssetPath(settings.LogoPath);
        LoadingPath = ResolveAssetPath(settings.LoadingPath);
        GameEntries = settings.Games.Select(MenuEntryViewModel.ForGame).ToList();
        OperationEntries = settings.Operations.Select(MenuEntryViewModel.ForOperation).ToList();
        ConfirmationChoices = [ConfirmationCancelText, ConfirmationAcceptText];
        UpdateReturnChoices = [localization.Get("UPDATE_ALL_RETURN")];
    }

    public event EventHandler? CloseRequested;

    public event EventHandler<WindowVisibilityEventArgs>? WindowVisibilityRequested;

    public string PlatformName { get; }

    public string LogoPath { get; }

    public string LoadingPath { get; }

    public IReadOnlyList<MenuEntryViewModel> GameEntries { get; }

    public IReadOnlyList<MenuEntryViewModel> OperationEntries { get; }

    public IReadOnlyList<string> ConfirmationChoices { get; }

    public IReadOnlyList<string> UpdateReturnChoices { get; private set; }

    public IReadOnlyList<GameUpdateItemViewModel> UpdateAllEntries
    {
        get => updateAllEntries;
        private set
        {
            updateAllEntries = value;
            RaisePropertyChanged();
        }
    }

    public string MenuTitle => localization.Get("OPERATION_MENU_TITLE");

    public string MenuHelp => localization.Get("OPERATION_MENU_HELP");

    public string GameListTitle => localization.Get("GAME_LIST_TITLE");

    public string OperationListTitle => localization.Get("MACHINE_OPERATION_LIST_TITLE");

    public string ConfirmationCancelText => localization.Get("CONFIRM_CANCEL");

    public string ConfirmationAcceptText => localization.Get("CONFIRM_ACCEPT");

    public bool IsBootVisible => Screen == LauncherScreen.Boot;

    public bool IsMenuVisible => Screen == LauncherScreen.Menu;

    public bool IsConfirmationVisible => Screen == LauncherScreen.Confirmation;

    public bool IsUpdateAllVisible => Screen == LauncherScreen.UpdateAll;

    public bool IsErrorVisible => Screen == LauncherScreen.Error;

    public DisplayLayoutMode ActiveLayoutMode
    {
        get => activeLayoutMode;
        private set => SetProperty(ref activeLayoutMode, value);
    }

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
            RaisePropertyChanged(nameof(IsConfirmationVisible));
            RaisePropertyChanged(nameof(IsUpdateAllVisible));
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

    public string BootUpdateProgressText
    {
        get => bootUpdateProgressText;
        private set => SetProperty(ref bootUpdateProgressText, value);
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

    public int SelectedGameIndex => menuSection == MenuSection.Games ? selectedGameIndex : -1;

    public int SelectedOperationIndex => menuSection == MenuSection.Operations ? selectedOperationIndex : -1;

    public int SelectedUpdateGameIndex =>
        updateAllSection == UpdateAllSection.Games ? selectedUpdateGameIndex : -1;

    public int SelectedUpdateReturnIndex => updateAllSection == UpdateAllSection.Return ? 0 : -1;

    public string UpdateAllTitle
    {
        get => updateAllTitle;
        private set => SetProperty(ref updateAllTitle, value);
    }

    public string UpdateAllHelp => localization.Get("UPDATE_ALL_HELP");

    public string UpdateGameListTitle => localization.Get("UPDATE_ALL_GAME_LIST_TITLE");

    public IReadOnlyList<string> UpdateCancelConfirmationChoices => ConfirmationChoices;

    public string UpdateCancelConfirmationTitle => localization.Get("UPDATE_ALL_CANCEL_CONFIRM_TITLE");

    public string UpdateCancelConfirmationMessage => localization.Get("UPDATE_ALL_CANCEL_CONFIRM_MESSAGE");

    public bool IsUpdateCancelConfirmationVisible
    {
        get => isUpdateCancelConfirmationVisible;
        private set => SetProperty(ref isUpdateCancelConfirmationVisible, value);
    }

    public int UpdateCancelConfirmationSelectedIndex
    {
        get => updateCancelConfirmationSelectedIndex;
        private set => SetProperty(ref updateCancelConfirmationSelectedIndex, value);
    }

    public bool IsUpdateDetailsVisible
    {
        get => isUpdateDetailsVisible;
        private set => SetProperty(ref isUpdateDetailsVisible, value);
    }

    public string UpdateDetailTitle
    {
        get => updateDetailTitle;
        private set => SetProperty(ref updateDetailTitle, value);
    }

    public string UpdateDetailMessage
    {
        get => updateDetailMessage;
        private set => SetProperty(ref updateDetailMessage, value);
    }

    public int ConfirmationSelectedIndex
    {
        get => confirmationSelectedIndex;
        private set => SetProperty(ref confirmationSelectedIndex, value);
    }

    public string ConfirmationTitle
    {
        get => confirmationTitle;
        private set => SetProperty(ref confirmationTitle, value);
    }

    public string ConfirmationMessage
    {
        get => confirmationMessage;
        private set => SetProperty(ref confirmationMessage, value);
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

            case LauncherScreen.Confirmation:
                HandleConfirmationInput(action);
                break;

            case LauncherScreen.UpdateAll:
                HandleUpdateAllInput(action);
                break;

            case LauncherScreen.Error when action is CabinetInputAction.Select or CabinetInputAction.Confirm:
                ShowMenu();
                break;
        }
    }

    private void HandleUpdateAllInput(CabinetInputAction action)
    {
        if (IsUpdateCancelConfirmationVisible)
        {
            HandleUpdateCancelConfirmationInput(action);
            return;
        }

        if (IsUpdateDetailsVisible)
        {
            if (action is CabinetInputAction.Confirm or CabinetInputAction.Select)
            {
                IsUpdateDetailsVisible = false;
            }

            return;
        }

        switch (action)
        {
            case CabinetInputAction.SwitchList when UpdateAllEntries.Count > 0:
                updateAllSection = updateAllSection == UpdateAllSection.Games
                    ? UpdateAllSection.Return
                    : UpdateAllSection.Games;
                RaiseUpdateAllSelectionChanged();
                break;
            case CabinetInputAction.Up when UpdateAllEntries.Count > 0:
                MoveUpdateAllSelection(-1);
                break;
            case CabinetInputAction.Down when UpdateAllEntries.Count > 0:
                MoveUpdateAllSelection(1);
                break;
            case CabinetInputAction.Confirm when updateAllSection == UpdateAllSection.Games:
                ShowUpdateDetails();
                break;
            case CabinetInputAction.Confirm when updateAllSection == UpdateAllSection.Return && updateAllRunning:
            case CabinetInputAction.Select when updateAllRunning:
                RequestUpdateCancellation();
                break;
            case CabinetInputAction.Confirm when updateAllSection == UpdateAllSection.Return && !updateAllRunning:
            case CabinetInputAction.Select when !updateAllRunning:
                ShowMenu();
                break;
        }
    }

    private void RequestUpdateCancellation()
    {
        if (updatePauseController is null)
        {
            return;
        }

        UpdateAllTitle = localization.Get("UPDATE_ALL_PAUSING_TITLE");
        updatePauseController.RequestPause();
    }

    private void HandleUpdateCancelConfirmationInput(CabinetInputAction action)
    {
        switch (action)
        {
            case CabinetInputAction.Up:
            case CabinetInputAction.Down:
                UpdateCancelConfirmationSelectedIndex = UpdateCancelConfirmationSelectedIndex == 0 ? 1 : 0;
                break;
            case CabinetInputAction.Select:
            case CabinetInputAction.Confirm when UpdateCancelConfirmationSelectedIndex == 0:
                IsUpdateCancelConfirmationVisible = false;
                UpdateAllTitle = localization.Get("UPDATE_ALL_RUNNING_TITLE");
                updatePauseController?.Resume();
                break;
            case CabinetInputAction.Confirm:
                IsUpdateCancelConfirmationVisible = false;
                updateAllRunning = false;
                var pauseController = updatePauseController;
                updatePauseController = null;
                ShowMenu();
                pauseController?.Resume();
                break;
        }
    }

    private void MoveUpdateAllSelection(int direction)
    {
        if (updateAllSection == UpdateAllSection.Return)
        {
            selectedUpdateGameIndex = direction < 0 ? UpdateAllEntries.Count - 1 : 0;
            updateAllSection = UpdateAllSection.Games;
            RaiseUpdateAllSelectionChanged();
            return;
        }

        if (direction < 0 && selectedUpdateGameIndex > 0)
        {
            selectedUpdateGameIndex--;
            RaisePropertyChanged(nameof(SelectedUpdateGameIndex));
        }
        else if (direction > 0 && selectedUpdateGameIndex < UpdateAllEntries.Count - 1)
        {
            selectedUpdateGameIndex++;
            RaisePropertyChanged(nameof(SelectedUpdateGameIndex));
        }
        else
        {
            updateAllSection = UpdateAllSection.Return;
            RaiseUpdateAllSelectionChanged();
        }
    }

    private void ShowUpdateDetails()
    {
        var entry = UpdateAllEntries.ElementAtOrDefault(selectedUpdateGameIndex);
        if (entry is null)
        {
            return;
        }

        UpdateDetailTitle = $"{entry.Title} ({entry.Id})";
        UpdateDetailMessage = !string.IsNullOrWhiteSpace(entry.Details)
            ? entry.Details
            : entry.HasCopyStatistics
                ? $"{entry.Status}{Environment.NewLine}{Environment.NewLine}{FormatFileCopySummary(entry.TotalFiles, entry.CopiedFiles)}"
                : localization.Get("UPDATE_DETAIL_WAITING");
        IsUpdateDetailsVisible = true;
    }

    private void RaiseUpdateAllSelectionChanged()
    {
        RaisePropertyChanged(nameof(SelectedUpdateGameIndex));
        RaisePropertyChanged(nameof(SelectedUpdateReturnIndex));
    }

    private void HandleMenuInput(CabinetInputAction action)
    {
        if (GameEntries.Count == 0 && OperationEntries.Count == 0)
        {
            return;
        }

        switch (action)
        {
            case CabinetInputAction.Up:
                MoveMenuSelection(-1);
                break;
            case CabinetInputAction.Down:
                MoveMenuSelection(1);
                break;
            case CabinetInputAction.SwitchList:
                SwitchMenuSection();
                break;
            case CabinetInputAction.Confirm:
                var entry = menuSection == MenuSection.Games
                    ? GameEntries.ElementAtOrDefault(selectedGameIndex)
                    : OperationEntries.ElementAtOrDefault(selectedOperationIndex);
                if (entry is not null)
                {
                    _ = ExecuteMenuEntryAsync(entry);
                }

                break;
        }
    }

    private void MoveMenuSelection(int direction)
    {
        if (menuSection == MenuSection.Games && GameEntries.Count > 0)
        {
            if (direction < 0 && selectedGameIndex > 0)
            {
                selectedGameIndex--;
                RaisePropertyChanged(nameof(SelectedGameIndex));
            }
            else if (direction > 0 && selectedGameIndex < GameEntries.Count - 1)
            {
                selectedGameIndex++;
                RaisePropertyChanged(nameof(SelectedGameIndex));
            }
            else if (OperationEntries.Count > 0)
            {
                selectedOperationIndex = direction > 0 ? 0 : OperationEntries.Count - 1;
                SetMenuSection(MenuSection.Operations);
            }

            return;
        }

        if (OperationEntries.Count > 0)
        {
            if (direction < 0 && selectedOperationIndex > 0)
            {
                selectedOperationIndex--;
                RaisePropertyChanged(nameof(SelectedOperationIndex));
            }
            else if (direction > 0 && selectedOperationIndex < OperationEntries.Count - 1)
            {
                selectedOperationIndex++;
                RaisePropertyChanged(nameof(SelectedOperationIndex));
            }
            else if (GameEntries.Count > 0)
            {
                selectedGameIndex = direction > 0 ? 0 : GameEntries.Count - 1;
                SetMenuSection(MenuSection.Games);
            }
        }
    }

    private void SwitchMenuSection()
    {
        if (GameEntries.Count > 0 && OperationEntries.Count > 0)
        {
            SetMenuSection(menuSection == MenuSection.Games ? MenuSection.Operations : MenuSection.Games);
        }
    }

    private void SetMenuSection(MenuSection value)
    {
        if (menuSection == value)
        {
            return;
        }

        menuSection = value;
        RaisePropertyChanged(nameof(SelectedGameIndex));
        RaisePropertyChanged(nameof(SelectedOperationIndex));
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

        if (entry.Operation.Confirmation.Enabled)
        {
            ShowConfirmation(entry.Operation);
            return;
        }

        await ExecuteOperationAsync(entry.Operation);
    }

    private void ShowConfirmation(OperationSettings operation)
    {
        pendingOperation = operation;
        ConfirmationSelectedIndex = 0;
        ConfirmationTitle = string.IsNullOrWhiteSpace(operation.Confirmation.Title)
            ? operation.Kind switch
            {
                OperationKind.Shutdown => localization.Get("SHUTDOWN_CONFIRM_TITLE"),
                OperationKind.Restart => localization.Get("RESTART_CONFIRM_TITLE"),
                _ => operation.Title
            }
            : operation.Confirmation.Title;
        ConfirmationMessage = string.IsNullOrWhiteSpace(operation.Confirmation.Message)
            ? operation.Kind == OperationKind.UpdateAllGames
                ? localization.Get("UPDATE_ALL_CONFIRM_MESSAGE")
                : localization.Get("POWER_CONFIRM_MESSAGE")
            : operation.Confirmation.Message;
        Screen = LauncherScreen.Confirmation;
    }

    private void HandleConfirmationInput(CabinetInputAction action)
    {
        switch (action)
        {
            case CabinetInputAction.Up:
            case CabinetInputAction.Down:
                ConfirmationSelectedIndex = ConfirmationSelectedIndex == 0 ? 1 : 0;
                break;
            case CabinetInputAction.Select:
                pendingOperation = null;
                ShowMenuCore();
                break;
            case CabinetInputAction.Confirm when ConfirmationSelectedIndex == 0:
                pendingOperation = null;
                ShowMenuCore();
                break;
            case CabinetInputAction.Confirm when pendingOperation is not null:
                var operation = pendingOperation;
                pendingOperation = null;
                _ = ExecuteOperationAsync(operation);
                break;
        }
    }

    private async Task ExecuteOperationAsync(OperationSettings operation)
    {
        if (operation.Kind == OperationKind.Exit)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (operation.Kind == OperationKind.UpdateAllGames)
        {
            await UpdateAllGamesAsync();
            return;
        }

        if (operation.Kind is OperationKind.Shutdown or OperationKind.Restart)
        {
            if (!operation.Command.Enabled)
            {
                log.Info($"Machine power action skipped because preview mode is active: {operation.Kind}.");
                ShowMenuCore();
                return;
            }

            if (machinePower.Execute(operation.Kind))
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ShowError("ERROR 9005", localization.Get("POWER_ACTION_FAILED"));
            }

            return;
        }

        var generation = BeginSession(out var token);
        Screen = LauncherScreen.Boot;
        StepLabel = "OPERATION";
        Message = operation.Title;
        IsBusy = true;
        var result = await launcher.LaunchAsync(operation.Command, token);
        if (!IsCurrent(generation))
        {
            return;
        }

        IsBusy = false;
        if (!result.Success)
        {
            ShowError("ERROR 9003", result.Error ?? localization.Get("LAUNCH_TARGET_FAILED"));
        }
        else if (operation.CloseAfterRun)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ShowMenuCore();
        }
    }

    private async Task UpdateAllGamesAsync()
    {
        var generation = BeginSession(out var token);
        ActiveLayoutMode = settings.Display.LayoutMode;
        UpdateAllEntries = settings.Games
            .Select(game => new GameUpdateItemViewModel(
                game,
                localization.Get("UPDATE_STATUS_WAITING"),
                UpdateStatusTone.Notice))
            .ToList();
        selectedUpdateGameIndex = 0;
        updateAllSection = UpdateAllSection.Return;
        updateAllRunning = true;
        UpdateReturnChoices = [localization.Get("UPDATE_ALL_CANCEL_AND_RETURN")];
        RaisePropertyChanged(nameof(UpdateReturnChoices));
        IsUpdateDetailsVisible = false;
        IsUpdateCancelConfirmationVisible = false;
        UpdateAllTitle = localization.Get("UPDATE_ALL_RUNNING_TITLE");
        IsBusy = true;
        bootSequenceActive = false;
        input.Resume();
        Screen = LauncherScreen.UpdateAll;
        RaiseUpdateAllSelectionChanged();

        try
        {
            // Give WPF a render pass before fast, local version checks start posting
            // status changes. Otherwise an entire update-all run can complete while
            // the confirmation page is still the last rendered frame.
            await Task.Delay(50, token);
            var pauseState = new Progress<bool>(paused =>
            {
                try
                {
                    if (!IsCurrent(generation) || !updateAllRunning)
                    {
                        return;
                    }

                    if (paused)
                    {
                        UpdateCancelConfirmationSelectedIndex = 0;
                        IsUpdateCancelConfirmationVisible = true;
                        UpdateAllTitle = localization.Get("UPDATE_ALL_PAUSED_TITLE");
                    }
                }
                catch (Exception exception)
                {
                    log.Error("Failed to update the update-all pause UI.", exception);
                }
            });
            updatePauseController = new UpdatePauseController(pauseState);
            log.Info($"Update-all operation started for {UpdateAllEntries.Count} configured game(s).");
            var hadFailures = false;
            foreach (var entry in UpdateAllEntries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    log.Info($"Update-all checking game '{entry.Game.Id}'.");
                    var progress = new Progress<GameUpdateProgress>(value =>
                    {
                        try
                        {
                            if (IsCurrent(generation))
                            {
                                ApplyUpdateProgress(entry, value);
                            }
                        }
                        catch (Exception exception)
                        {
                            log.Error($"Failed to display update progress for game '{entry.Game.Id}'.", exception);
                        }
                    });
                    var result = await Task.Run(
                        () => updater.TryUpdateAsync(
                            entry.Game,
                            settings.UpdateSources,
                            token,
                            progress,
                            updatePauseController),
                        token);
                    entry.Apply(
                        GetUpdateStatus(result.Outcome),
                        FormatUpdateDetails(result),
                        GetUpdateStatusTone(result.Outcome),
                        result.TotalFiles,
                        result.CopiedFiles,
                        result.HasCopyStatistics);
                    log.Info($"Update-all result for game '{entry.Game.Id}': {result.Outcome}.");
                    hadFailures |= result.Outcome == GameUpdateOutcome.Failed;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    hadFailures = true;
                    log.Error($"Unexpected update failure for game '{entry.Game.Id}'.", exception);
                    entry.Apply(
                        localization.Get("UPDATE_STATUS_FAILED"),
                        FormatUnexpectedUpdateFailure(entry, exception),
                        UpdateStatusTone.Failure);
                }
            }

            if (IsCurrent(generation))
            {
                log.Info("Update-all operation completed.");
                UpdateAllTitle = localization.Get(hadFailures
                    ? "UPDATE_ALL_COMPLETE_WITH_ERRORS_TITLE"
                    : "UPDATE_ALL_COMPLETE_TITLE");
                updateAllRunning = false;
                updatePauseController = null;
                UpdateReturnChoices = [localization.Get("UPDATE_ALL_RETURN")];
                RaisePropertyChanged(nameof(UpdateReturnChoices));
                RaiseUpdateAllSelectionChanged();
                IsBusy = false;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            log.Info("Update-all operation cancelled.");
        }
        catch (Exception exception)
        {
            log.Error("Unexpected update-all failure.", exception);
            if (IsCurrent(generation))
            {
                UpdateAllTitle = localization.Get("UPDATE_ALL_COMPLETE_WITH_ERRORS_TITLE");
                updateAllRunning = false;
                updatePauseController = null;
                IsUpdateCancelConfirmationVisible = false;
                UpdateReturnChoices = [localization.Get("UPDATE_ALL_RETURN")];
                RaisePropertyChanged(nameof(UpdateReturnChoices));
                RaiseUpdateAllSelectionChanged();
                IsBusy = false;
            }
        }
    }

    private void ApplyUpdateProgress(GameUpdateItemViewModel entry, GameUpdateProgress progress)
    {
        var status = GetUpdateProgressStatus(progress.Stage);
        var progressText = progress.Stage switch
        {
            GameUpdateProgressStage.Downloading => string.Format(
                localization.Get("UPDATE_PROGRESS_DOWNLOADED"),
                progress.CompletedFiles,
                progress.TotalFiles,
                (int)progress.Percentage),
            GameUpdateProgressStage.Copying => string.Format(
                localization.Get("UPDATE_PROGRESS_FILES"),
                progress.CompletedFiles,
                progress.TotalFiles,
                (int)progress.Percentage),
            _ => status
        };
        entry.ApplyProgress(
            status,
            progressText,
            progress.CompletedFiles,
            progress.TotalFiles,
            !progress.HasFileCount,
            progress.Stage == GameUpdateProgressStage.Copying);
    }

    private string GetUpdateProgressStatus(GameUpdateProgressStage stage, string? language = null) =>
        localization.Get(stage switch
        {
            GameUpdateProgressStage.Checking => "UPDATE_STATUS_CHECKING",
            GameUpdateProgressStage.Downloading => "UPDATE_STATUS_DOWNLOADING",
            GameUpdateProgressStage.Preparing => "UPDATE_STATUS_PREPARING",
            _ => "UPDATE_STATUS_COPYING"
        }, language);

    private string GetUpdateStatus(GameUpdateOutcome outcome) => localization.Get(GetUpdateStatusKey(outcome));

    private static string GetUpdateStatusKey(GameUpdateOutcome outcome) => outcome switch
    {
        GameUpdateOutcome.UpdateDisabled => "UPDATE_STATUS_DISABLED",
        GameUpdateOutcome.NoSources => "UPDATE_STATUS_NO_SOURCE",
        GameUpdateOutcome.UpdateDirectoryNotFound => "UPDATE_STATUS_DIRECTORY_NOT_FOUND",
        GameUpdateOutcome.ServerUnavailable => "UPDATE_STATUS_SERVER_UNAVAILABLE",
        GameUpdateOutcome.UpToDate => "UPDATE_STATUS_UP_TO_DATE",
        GameUpdateOutcome.Completed => "UPDATE_STATUS_COMPLETED",
        _ => "UPDATE_STATUS_FAILED"
    };

    private static UpdateStatusTone GetUpdateStatusTone(GameUpdateOutcome outcome) => outcome switch
    {
        GameUpdateOutcome.Completed => UpdateStatusTone.Success,
        GameUpdateOutcome.Failed => UpdateStatusTone.Failure,
        GameUpdateOutcome.UpdateDisabled => UpdateStatusTone.Neutral,
        _ => UpdateStatusTone.Notice
    };

    private string FormatUpdateDetails(GameUpdateResult result)
    {
        var builder = new StringBuilder();
        builder.Append(localization.Get("UPDATE_DETAIL_OVERALL"));
        builder.Append(' ');
        builder.AppendLine(GetUpdateStatus(result.Outcome));

        if (result.Outcome is GameUpdateOutcome.Completed or GameUpdateOutcome.Failed)
        {
            builder.AppendLine(result.HasCopyStatistics
                ? FormatFileCopySummary(result.TotalFiles, result.CopiedFiles)
                : string.Format(localization.Get("UPDATE_DETAIL_FILE_SUMMARY_UNKNOWN"), result.CopiedFiles));
        }

        if (result.Attempts.Count == 0)
        {
            builder.AppendLine(result.Outcome == GameUpdateOutcome.UpdateDisabled
                ? localization.Get("UPDATE_DETAIL_DISABLED")
                : localization.Get("UPDATE_DETAIL_NO_SOURCES"));
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine();
        builder.AppendLine(localization.Get("UPDATE_DETAIL_SOURCE_RESULTS"));
        foreach (var attempt in result.Attempts)
        {
            builder.Append("• ");
            builder.Append(attempt.SourceId);
            builder.Append(": ");
            builder.AppendLine(GetUpdateSourceStatus(attempt.Outcome));
            if (!string.IsNullOrWhiteSpace(attempt.Detail))
            {
                builder.Append("  ");
                builder.AppendLine(attempt.Detail);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string FormatUnexpectedUpdateFailure(
        GameUpdateItemViewModel entry,
        Exception exception)
    {
        var builder = new StringBuilder();
        builder.Append(localization.Get("UPDATE_DETAIL_OVERALL"));
        builder.Append(' ');
        builder.AppendLine(localization.Get("UPDATE_STATUS_FAILED"));
        builder.AppendLine(entry.HasCopyStatistics
            ? FormatFileCopySummary(entry.TotalFiles, entry.CopiedFiles)
            : string.Format(localization.Get("UPDATE_DETAIL_FILE_SUMMARY_UNKNOWN"), entry.CopiedFiles));
        builder.AppendLine();
        builder.Append(exception);
        return builder.ToString();
    }

    private string FormatFileCopySummary(int totalFiles, int copiedFiles) =>
        string.Format(
            localization.Get("UPDATE_DETAIL_FILE_SUMMARY"),
            totalFiles,
            copiedFiles);

    private string GetUpdateSourceStatus(UpdateSourceOutcome outcome) => outcome switch
    {
        UpdateSourceOutcome.NotAttempted => localization.Get("UPDATE_SOURCE_NOT_ATTEMPTED"),
        UpdateSourceOutcome.Missing => localization.Get("UPDATE_SOURCE_MISSING"),
        UpdateSourceOutcome.Disabled => localization.Get("UPDATE_SOURCE_DISABLED"),
        UpdateSourceOutcome.DirectoryNotFound => localization.Get("UPDATE_SOURCE_DIRECTORY_NOT_FOUND"),
        UpdateSourceOutcome.ServerUnavailable => localization.Get("UPDATE_SOURCE_SERVER_UNAVAILABLE"),
        UpdateSourceOutcome.ConnectionFailed => localization.Get("UPDATE_SOURCE_CONNECTION_FAILED"),
        UpdateSourceOutcome.UpToDate => localization.Get("UPDATE_SOURCE_UP_TO_DATE"),
        UpdateSourceOutcome.Completed => localization.Get("UPDATE_SOURCE_COMPLETED"),
        _ => localization.Get("UPDATE_SOURCE_FAILED")
    };

    private async Task RunGameAsync(GameSettings game)
    {
        var generation = BeginSession(out var token);
        ActiveLayoutMode = game.LayoutMode ?? settings.Display.LayoutMode;
        activeGameLanguage = string.IsNullOrWhiteSpace(game.Language) ? settings.Language : game.Language;
        input.Resume();
        WindowVisibilityRequested?.Invoke(this, new WindowVisibilityEventArgs(true));
        Screen = LauncherScreen.Boot;
        IsBusy = true;
        bootSequenceActive = false;
        bootControl = new BootSequenceControl();

        try
        {
            bootSequenceActive = true;
            var timeline = game.Timeline.Count > 0 ? game.Timeline : settings.Timeline;
            var result = await sequence.RunAsync(
                timeline,
                game.Launch,
                ShowPhase,
                bootControl,
                game.Update.Enabled
                    ? cancellationToken => UpdateBootGameAsync(game, generation, cancellationToken)
                    : null,
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

            StepLabel = "STEP 30";
            Message = localization.Get("STEP_30_MESSAGE", activeGameLanguage);
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

            if (game.Monitor.ReadyDelayMs > 0)
            {
                log.Info($"Game target ready; waiting {game.Monitor.ReadyDelayMs} ms before hiding the boot screen.");
                await Task.Delay(game.Monitor.ReadyDelayMs, token);
                if (!IsCurrent(generation))
                {
                    return;
                }
            }

            IsBusy = false;
            _ = monitor.TryActivateTargetWindow(game.Monitor, settings.Display.HideCursor);
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

    private async Task UpdateBootGameAsync(GameSettings game, int generation, CancellationToken cancellationToken)
    {
        var updating = true;
        BootUpdateProgressText = string.Empty;
        var progress = new Progress<GameUpdateProgress>(value =>
        {
            try
            {
                if (!updating || !IsCurrent(generation) || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!value.HasFileCount)
                {
                    BootUpdateProgressText = string.Empty;
                    return;
                }

                var status = GetUpdateProgressStatus(value.Stage, activeGameLanguage);
                BootUpdateProgressText = $" · {status} {(int)value.Percentage}%";
            }
            catch (Exception exception)
            {
                log.Error($"Failed to display boot update progress for game '{game.Id}'.", exception);
            }
        });

        try
        {
            var result = await Task.Run(
                () => updater.TryUpdateAsync(game, settings.UpdateSources, cancellationToken, progress),
                cancellationToken);
            if (IsCurrent(generation) && !cancellationToken.IsCancellationRequested)
            {
                var status = localization.Get(GetUpdateStatusKey(result.Outcome), activeGameLanguage);
                var percentage = result.Outcome is GameUpdateOutcome.Completed or GameUpdateOutcome.UpToDate
                    ? 100
                    : result.TotalFiles == 0 ? 0 : (int)Math.Clamp(result.CopiedFiles * 100.0 / result.TotalFiles, 0, 100);
                BootUpdateProgressText = $" · {status} {percentage}%";
            }
        }
        finally
        {
            // Queued UI callbacks must not overwrite the next boot phase or another session.
            updating = false;
        }
    }

    private void ShowPhase(BootPhase phase)
    {
        BootUpdateProgressText = string.Empty;
        Screen = LauncherScreen.Boot;
        IsBusy = true;
        StepLabel = $"STEP {phase.Step:00}";
        Message = localization.Get(phase.MessageKey, activeGameLanguage);
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
        ActiveLayoutMode = settings.Display.LayoutMode;
        input.Resume();
        bootSequenceActive = false;
        IsBusy = false;
        Screen = LauncherScreen.Menu;
        pendingOperation = null;
        if (selectedGameIndex >= GameEntries.Count)
        {
            selectedGameIndex = 0;
        }

        if (selectedOperationIndex >= OperationEntries.Count)
        {
            selectedOperationIndex = 0;
        }

        if (menuSection == MenuSection.Games && GameEntries.Count == 0)
        {
            menuSection = MenuSection.Operations;
        }
        else if (menuSection == MenuSection.Operations && OperationEntries.Count == 0)
        {
            menuSection = MenuSection.Games;
        }

        RaisePropertyChanged(nameof(SelectedGameIndex));
        RaisePropertyChanged(nameof(SelectedOperationIndex));
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
        updater.Dispose();
        lifetime.Dispose();
    }
}
