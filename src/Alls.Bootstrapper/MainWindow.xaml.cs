using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Alls.Bootstrapper.Models;
using Alls.Bootstrapper.Services;
using Alls.Bootstrapper.ViewModels;

namespace Alls.Bootstrapper;

public partial class MainWindow : Window
{
    private readonly DisplaySettings display;
    private readonly InputSettings inputSettings;
    private readonly LauncherViewModel viewModel;
    private readonly IInputService input;
    private readonly ILogService log;
    private bool isClosing;

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
        Activate();
        Focus();
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

        var useCabinetLayout = display.LayoutMode == DisplayLayoutMode.Cabinet
            || (display.LayoutMode == DisplayLayoutMode.Auto && height > width);
        if (!useCabinetLayout)
        {
            DesignViewport.Width = double.NaN;
            DesignViewport.Height = double.NaN;
            DesignViewport.Margin = new Thickness(0);
            DesignViewport.HorizontalAlignment = HorizontalAlignment.Stretch;
            DesignViewport.VerticalAlignment = VerticalAlignment.Stretch;
            return;
        }

        if (height > width)
        {
            var stageSize = Math.Min(width, height * 0.75);
            var contentHeight = stageSize * 720.0 / 1280.0;
            var bottomMargin = height * 0.015;
            var stageTop = height - stageSize - bottomMargin;
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
        if (e.PropertyName == nameof(LauncherViewModel.SelectedIndex)
            && viewModel.SelectedIndex >= 0
            && viewModel.SelectedIndex < viewModel.MenuEntries.Count)
        {
            GameMenu.ScrollIntoView(viewModel.MenuEntries[viewModel.SelectedIndex]);
            return;
        }

        if (e.PropertyName is not (nameof(LauncherViewModel.StepLabel) or nameof(LauncherViewModel.Message)))
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = 0.25,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        StatusPanel.BeginAnimation(OpacityProperty, animation);
    }

    private void OnWindowVisibilityRequested(object? sender, WindowVisibilityEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Visible)
            {
                Show();
                ConfigureWindow();
                Activate();
                Focus();
            }
            else
            {
                Hide();
            }
        });
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
}
