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
    private readonly BootViewModel viewModel;
    private readonly ILogService log;
    private bool isClosing;

    internal MainWindow(DisplaySettings display, BootViewModel viewModel, ILogService log)
    {
        this.display = display;
        this.viewModel = viewModel;
        this.log = log;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureWindow();
        Activate();
        Focus();
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
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (display.AllowEscapeToExit && e.Key == Key.Escape)
        {
            CloseSafely();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(BootViewModel.StepLabel) or nameof(BootViewModel.Message)))
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
        viewModel.CloseRequested -= OnCloseRequested;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.Dispose();
        log.Info("ALLS bootstrapper stopped.");
        Application.Current.Shutdown();
    }
}
