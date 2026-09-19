using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Alls.Bootstrapper.Controls;

public sealed class AnimatedGifImage : Image
{
    public static readonly DependencyProperty GifSourceProperty = DependencyProperty.Register(
        nameof(GifSource),
        typeof(string),
        typeof(AnimatedGifImage),
        new FrameworkPropertyMetadata(string.Empty, OnGifSourceChanged));

    public static readonly DependencyProperty IsAnimatingProperty = DependencyProperty.Register(
        nameof(IsAnimating),
        typeof(bool),
        typeof(AnimatedGifImage),
        new FrameworkPropertyMetadata(true, OnIsAnimatingChanged));

    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private readonly List<BitmapFrame> frames = [];
    private readonly List<TimeSpan> frameDurations = [];
    private int frameIndex;

    public AnimatedGifImage()
    {
        timer.Tick += OnTimerTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string GifSource
    {
        get => (string)GetValue(GifSourceProperty);
        set => SetValue(GifSourceProperty, value);
    }

    public bool IsAnimating
    {
        get => (bool)GetValue(IsAnimatingProperty);
        set => SetValue(IsAnimatingProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadFrames();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        timer.Stop();
    }

    private void LoadFrames()
    {
        timer.Stop();
        frames.Clear();
        frameDurations.Clear();
        frameIndex = 0;
        Source = null;

        if (string.IsNullOrWhiteSpace(GifSource) || !File.Exists(GifSource))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(GifSource);
            var decoder = new GifBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            foreach (var decoderFrame in decoder.Frames)
            {
                var frame = BitmapFrame.Create(decoderFrame);
                frame.Freeze();
                frames.Add(frame);
                frameDurations.Add(ReadFrameDuration(decoderFrame.Metadata as BitmapMetadata));
            }

            if (frames.Count == 0)
            {
                return;
            }

            Source = frames[0];
            StartAnimationIfNeeded();
        }
        catch (IOException)
        {
            Source = null;
        }
        catch (NotSupportedException)
        {
            Source = null;
        }
    }

    private void StartAnimationIfNeeded()
    {
        timer.Stop();
        if (!IsLoaded || !IsAnimating || frames.Count <= 1)
        {
            return;
        }

        timer.Interval = frameDurations[frameIndex];
        timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        frameIndex = (frameIndex + 1) % frames.Count;
        Source = frames[frameIndex];
        timer.Interval = frameDurations[frameIndex];
    }

    private static TimeSpan ReadFrameDuration(BitmapMetadata? metadata)
    {
        const int defaultDelayMilliseconds = 60;
        if (metadata is null)
        {
            return TimeSpan.FromMilliseconds(defaultDelayMilliseconds);
        }

        try
        {
            var rawDelay = metadata.GetQuery("/grctlext/Delay");
            var centiseconds = rawDelay is null ? 0 : Convert.ToInt32(rawDelay);
            return TimeSpan.FromMilliseconds(centiseconds > 1
                ? Math.Max(20, centiseconds * 10)
                : defaultDelayMilliseconds);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return TimeSpan.FromMilliseconds(defaultDelayMilliseconds);
        }
    }

    private static void OnGifSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var image = (AnimatedGifImage)dependencyObject;
        if (image.IsLoaded)
        {
            image.LoadFrames();
        }
    }

    private static void OnIsAnimatingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var image = (AnimatedGifImage)dependencyObject;
        image.StartAnimationIfNeeded();
    }
}
