using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Alls.Bootstrapper.Models;
using Alls.Bootstrapper.Services;
using Alls.Bootstrapper.ViewModels;

namespace Alls.Bootstrapper;

public partial class MainWindow : Window
{
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;
    private const int SwShow = 5;

    private readonly DisplaySettings display;
    private readonly InputSettings inputSettings;
    private readonly LauncherViewModel viewModel;
    private readonly IInputService input;
    private readonly ILogService log;
    private bool isClosing;
    private bool keyboardSwitchChordActive;
    private bool launcherVisible = true;
    private int foregroundRestoreGeneration;

    internal MainWindow(
        DisplaySettings display,
        InputSettings inputSettings,
        LauncherViewModel viewModel,
        IInputService input,
        ILogService log)
    {
        this.display = display;
        this.inputSettings = inputSettings;
        this.viewModel = viewModel;
        this.input = input;
        this.log = log;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
        SizeChanged += OnWindowSizeChanged;
        Closed += OnClosed;
        input.Pressed += OnCabinetButtonPressed;
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.WindowVisibilityRequested += OnWindowVisibilityRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureWindow();
        ApplyDisplayLayout();
        RestoreLauncherForeground();
        input.Start();
        await viewModel.StartAsync();
    }

    private void ConfigureWindow()
    {
        Topmost = display.Topmost;
        Cursor = display.HideCursor ? Cursors.None : Cursors.Arrow;

        if (display.Mode == WindowMode.Fullscreen)
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            WindowState = WindowState.Normal;
        }
        else
        {
            Width = display.Width;
            Height = display.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        ApplyDisplayLayout();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyDisplayLayout();
    }

    private void ApplyDisplayLayout()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var isPortrait = height > width;
        var layoutMode = ResolveLayoutMode(viewModel.ActiveLayoutMode, isPortrait);
        ApplyBootVisualMetrics(layoutMode, isPortrait);
        UpdateMaimaiButtonGuide(layoutMode, isPortrait, width, height);

        if (layoutMode == DisplayLayoutMode.Chunithm)
        {
            UseFullWindowLayout();
            return;
        }

        if (layoutMode is DisplayLayoutMode.Ongeki or DisplayLayoutMode.CardMaker)
        {
            if (!isPortrait)
            {
                UseFullWindowLayout();
                return;
            }

            var contentWidth = Math.Min(width, height * 1280.0 / 720.0);
            var contentHeight = contentWidth * 720.0 / 1280.0;
            DesignViewport.Width = contentWidth;
            DesignViewport.Height = contentHeight;
            DesignViewport.Margin = new Thickness(0, (height - contentHeight) / 2, 0, 0);
            DesignViewport.HorizontalAlignment = HorizontalAlignment.Center;
            DesignViewport.VerticalAlignment = VerticalAlignment.Top;
            return;
        }

        if (isPortrait)
        {
            var stageSize = Math.Min(width, height);
            var contentHeight = stageSize * 720.0 / 1280.0;
            var stageTop = height - stageSize;
            DesignViewport.Width = stageSize;
            DesignViewport.Height = contentHeight;
            DesignViewport.Margin = new Thickness(0, stageTop + (stageSize - contentHeight) / 2, 0, 0);
            DesignViewport.HorizontalAlignment = HorizontalAlignment.Center;
            DesignViewport.VerticalAlignment = VerticalAlignment.Top;
            return;
        }

        var landscapeStageSize = Math.Min(width * 0.48, height * 0.64);
        var landscapeContentHeight = landscapeStageSize * 720.0 / 1280.0;
        var centerX = Math.Clamp(width * 0.615, landscapeStageSize / 2, width - landscapeStageSize / 2);
        DesignViewport.Width = landscapeStageSize;
        DesignViewport.Height = landscapeContentHeight;
        DesignViewport.Margin = new Thickness(
            centerX - landscapeStageSize / 2,
            height * 0.34 + (landscapeStageSize - landscapeContentHeight) / 2,
            0,
            0);
        DesignViewport.HorizontalAlignment = HorizontalAlignment.Left;
        DesignViewport.VerticalAlignment = VerticalAlignment.Top;
    }

    private static DisplayLayoutMode ResolveLayoutMode(DisplayLayoutMode configuredMode, bool isPortrait)
    {
        return configuredMode switch
        {
            DisplayLayoutMode.Auto => isPortrait
                ? DisplayLayoutMode.MaimaiDx
                : DisplayLayoutMode.Chunithm,
            DisplayLayoutMode.Landscape => DisplayLayoutMode.Chunithm,
            DisplayLayoutMode.Cabinet => DisplayLayoutMode.MaimaiDx,
            _ => configuredMode
        };
    }

    private void UseFullWindowLayout()
    {
        DesignViewport.Width = double.NaN;
        DesignViewport.Height = double.NaN;
        DesignViewport.Margin = new Thickness(0);
        DesignViewport.HorizontalAlignment = HorizontalAlignment.Stretch;
        DesignViewport.VerticalAlignment = VerticalAlignment.Stretch;
    }

    private void ApplyBootVisualMetrics(DisplayLayoutMode layoutMode, bool isPortrait)
    {
        var usePortraitMetrics = isPortrait && layoutMode is
            DisplayLayoutMode.MaimaiDx or
            DisplayLayoutMode.Ongeki or
            DisplayLayoutMode.CardMaker;

        if (usePortraitMetrics)
        {
            // The portrait stage is still authored on a 1280x720 design surface.
            // These values compensate for its 1080/1280 scale on a 1080-wide cabinet display.
            BootLogo.Width = 560;
            BootLogo.Height = 393;
            BootLogo.Margin = new Thickness(0, 35, 0, 0);
            BootStatusStack.Width = 960;
            BootStatusStack.Margin = new Thickness(0, 500, 0, 0);
            BootPlatformText.FontSize = 43;
            BootStepText.FontSize = 43;
            BootMessageText.FontSize = 43;
            BootLoadingIndicator.Width = 46;
            BootLoadingIndicator.Height = 46;
            ErrorLogo.Width = 560;
            ErrorLogo.Height = 393;
            ErrorLogo.Margin = new Thickness(0, 35, 0, 0);
            ErrorStatusStack.Width = 960;
            ErrorStatusStack.Margin = new Thickness(0, 500, 0, 0);
            ErrorPlatformText.FontSize = 31;
            ErrorTitleText.FontSize = 52;
            ErrorMessageText.FontSize = 29;
            return;
        }

        BootLogo.Width = 430;
        BootLogo.Height = 300;
        BootLogo.Margin = new Thickness(0, 68, 0, 0);
        BootStatusStack.Width = 840;
        BootStatusStack.Margin = new Thickness(0, 374, 0, 0);
        BootPlatformText.FontSize = 34;
        BootStepText.FontSize = 26;
        BootMessageText.FontSize = 25;
        BootLoadingIndicator.Width = 46;
        BootLoadingIndicator.Height = 46;
        ErrorLogo.Width = 430;
        ErrorLogo.Height = 300;
        ErrorLogo.Margin = new Thickness(0, 68, 0, 0);
        ErrorStatusStack.Width = 900;
        ErrorStatusStack.Margin = new Thickness(0, 376, 0, 0);
        ErrorPlatformText.FontSize = 25;
        ErrorTitleText.FontSize = 45;
        ErrorMessageText.FontSize = 23;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (display.AllowEscapeToExit && e.Key == Key.Escape)
        {
            CloseSafely();
            return;
        }

        if (!inputSettings.KeyboardFallback)
        {
            return;
        }

        var switchChordPressed = Keyboard.IsKeyDown(Key.D2) && Keyboard.IsKeyDown(Key.D7);
        if (switchChordPressed)
        {
            if (!keyboardSwitchChordActive)
            {
                keyboardSwitchChordActive = true;
                viewModel.HandleInput(CabinetInputAction.SwitchList);
            }

            e.Handled = true;
            return;
        }

        var action = e.Key switch
        {
            Key.D1 or Key.NumPad8 or Key.Up => CabinetInputAction.Up,
            Key.D4 or Key.NumPad2 or Key.Down => CabinetInputAction.Down,
            Key.D5 or Key.Enter or Key.Space => CabinetInputAction.Confirm,
            Key.S or Key.Tab => CabinetInputAction.Select,
            _ => (CabinetInputAction?)null
        };
        if (action.HasValue)
        {
            viewModel.HandleInput(action.Value);
            e.Handled = true;
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.D2 or Key.D7)
        {
            keyboardSwitchChordActive = Keyboard.IsKeyDown(Key.D2) && Keyboard.IsKeyDown(Key.D7);
        }
    }

    private void OnCabinetButtonPressed(object? sender, CabinetInputEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            log.Info($"Cabinet input: P{e.Player} {e.Action}.");
            viewModel.HandleInput(e.Action);
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LauncherViewModel.ActiveLayoutMode)
            or nameof(LauncherViewModel.Screen)
            or nameof(LauncherViewModel.IsMenuVisible))
        {
            ApplyDisplayLayout();
            return;
        }

        if (e.PropertyName == nameof(LauncherViewModel.SelectedGameIndex)
            && viewModel.SelectedGameIndex >= 0
            && viewModel.SelectedGameIndex < viewModel.GameEntries.Count)
        {
            GameMenu.ScrollIntoView(viewModel.GameEntries[viewModel.SelectedGameIndex]);
            return;
        }

        if (e.PropertyName == nameof(LauncherViewModel.SelectedOperationIndex)
            && viewModel.SelectedOperationIndex >= 0
            && viewModel.SelectedOperationIndex < viewModel.OperationEntries.Count)
        {
            OperationMenu.ScrollIntoView(viewModel.OperationEntries[viewModel.SelectedOperationIndex]);
            return;
        }

        if (e.PropertyName == nameof(LauncherViewModel.SelectedUpdateGameIndex)
            && viewModel.SelectedUpdateGameIndex >= 0
            && viewModel.SelectedUpdateGameIndex < viewModel.UpdateAllEntries.Count)
        {
            UpdateGameList.ScrollIntoView(viewModel.UpdateAllEntries[viewModel.SelectedUpdateGameIndex]);
            return;
        }

        if (e.PropertyName is not (nameof(LauncherViewModel.StepLabel) or nameof(LauncherViewModel.Message)))
        {
            return;
        }

        if (display.StepTransitionMs <= 0)
        {
            StatusPanel.BeginAnimation(OpacityProperty, null);
            StatusPanel.Opacity = 1;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = 0.25,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(display.StepTransitionMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        StatusPanel.BeginAnimation(OpacityProperty, animation);
    }

    private void UpdateMaimaiButtonGuide(
        DisplayLayoutMode layoutMode,
        bool isPortrait,
        double width,
        double height)
    {
        var showMenuGuide = viewModel.IsMenuVisible;
        var showConfirmationGuide = viewModel.IsConfirmationVisible;
        var showUpdateGuide = viewModel.IsUpdateAllVisible;
        var showErrorGuide = viewModel.IsErrorVisible;
        if (layoutMode != DisplayLayoutMode.MaimaiDx
            || !isPortrait
            || (!showMenuGuide && !showConfirmationGuide && !showUpdateGuide && !showErrorGuide))
        {
            MaimaiButtonGuide.Visibility = Visibility.Collapsed;
            return;
        }

        var stageSize = Math.Min(width, height);
        var stageTop = height - stageSize;
        var centerX = width / 2;
        var centerY = stageTop + stageSize / 2;
        var buttonWidth = Math.Clamp(stageSize * 0.205, 150, 232);
        var buttonHeight = Math.Clamp(stageSize * 0.072, 58, 82);
        // Place the outer edge of each radial button directly on the cabinet circle.
        var radius = (stageSize - buttonHeight) / 2;
        var buttons = new (Grid Element, int Number)[]
        {
            (MaimaiGuideButton1, 1),
            (MaimaiGuideButton2, 2),
            (MaimaiGuideButton4, 4),
            (MaimaiGuideButton5, 5),
            (MaimaiGuideButton7, 7)
        };

        MaimaiGuideButton1.Visibility = showErrorGuide ? Visibility.Collapsed : Visibility.Visible;
        MaimaiGuideButton2.Visibility = showMenuGuide || showUpdateGuide
            ? Visibility.Visible
            : Visibility.Collapsed;
        MaimaiGuideButton4.Visibility = showErrorGuide ? Visibility.Collapsed : Visibility.Visible;
        MaimaiGuideButton7.Visibility = showMenuGuide || showUpdateGuide
            ? Visibility.Visible
            : Visibility.Collapsed;
        MaimaiButtonGuide.Width = width;
        MaimaiButtonGuide.Height = height;
        foreach (var (button, number) in buttons)
        {
            var angleDegrees = -67.5 + ((number - 1) * 45);
            var angle = angleDegrees * Math.PI / 180;
            button.Width = buttonWidth;
            button.Height = buttonHeight;
            button.RenderTransform = new RotateTransform(angleDegrees + 90);
            if (button.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            {
                label.FontSize = buttonHeight * 0.27;
                label.RenderTransformOrigin = new Point(0.5, 0.5);
                label.RenderTransform = number is >= 3 and <= 6
                    ? new RotateTransform(180)
                    : Transform.Identity;
            }

            Canvas.SetLeft(button, centerX + (Math.Cos(angle) * radius) - buttonWidth / 2);
            Canvas.SetTop(button, centerY + (Math.Sin(angle) * radius) - buttonHeight / 2);
        }

        MaimaiButtonGuide.Visibility = Visibility.Visible;
    }

    private void OnWindowVisibilityRequested(object? sender, WindowVisibilityEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Visible)
            {
                launcherVisible = true;
                var generation = ++foregroundRestoreGeneration;
                Show();
                ConfigureWindow();
                RestoreLauncherForeground();
                _ = ReinforceForegroundAsync(generation);
            }
            else
            {
                launcherVisible = false;
                foregroundRestoreGeneration++;
                Topmost = false;
                Hide();
            }
        });
    }

    private async Task ReinforceForegroundAsync(int generation)
    {
        await Task.Delay(100);
        if (!launcherVisible || generation != foregroundRestoreGeneration || isClosing)
        {
            return;
        }

        RestoreLauncherForeground();
        await Task.Delay(250);
        if (launcherVisible && generation == foregroundRestoreGeneration && !isClosing)
        {
            RestoreLauncherForeground();
        }
    }

    private void RestoreLauncherForeground()
    {
        if (!IsVisible || !OperatingSystem.IsWindows())
        {
            Activate();
            Focus();
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = ShowWindow(handle, SwShow);
        _ = SetWindowPos(
            handle,
            display.Topmost ? HwndTopmost : HwndTop,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpShowWindow);

        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = BringWindowToTop(handle);
            _ = SetForegroundWindow(handle);
            _ = SetActiveWindow(handle);
            _ = SetFocus(handle);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        Activate();
        Focus();
        Keyboard.Focus(this);
        if (display.HideCursor)
        {
            _ = SetCursor(IntPtr.Zero);
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(CloseSafely);
    }

    private void CloseSafely()
    {
        if (isClosing)
        {
            return;
        }

        isClosing = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        input.Pressed -= OnCabinetButtonPressed;
        SizeChanged -= OnWindowSizeChanged;
        viewModel.CloseRequested -= OnCloseRequested;
        viewModel.WindowVisibilityRequested -= OnWindowVisibilityRequested;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        input.Dispose();
        viewModel.Dispose();
        log.Info("ALLS bootstrapper stopped.");
        Application.Current.Shutdown();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);
}
