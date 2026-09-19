using System.Globalization;
using HidSharp;
using Alls.Bootstrapper.Models;

namespace Alls.Bootstrapper.Services;

internal sealed class MaimaiHidInputService : IInputService
{
    private readonly InputSettings settings;
    private readonly ILogService log;
    private readonly object deviceOpenGate = new();
    private readonly object stateGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<Task> readers = [];
    private readonly HashSet<HidStream> openStreams = [];
    private CancellationTokenSource? readerSession;
    private bool started;
    private bool disposed;

    public MaimaiHidInputService(InputSettings settings, ILogService log)
    {
        this.settings = settings;
        this.log = log;
    }

    public event EventHandler<CabinetInputEventArgs>? Pressed;

    public void Start()
    {
        if (started || !settings.Enabled)
        {
            return;
        }

        started = true;
        Resume();
    }

    public void Resume()
    {
        CancellationToken cancellationToken;
        lock (stateGate)
        {
            if (disposed || !started || !settings.Enabled || readerSession is not null)
            {
                return;
            }

            readerSession = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            cancellationToken = readerSession.Token;
        }

        foreach (var device in settings.Devices.Where(device => device.Enabled))
        {
            readers.Add(Task.Run(() => RunDeviceLoopAsync(device, cancellationToken)));
        }

        log.Info("Cabinet HID input resumed.");
    }

    public void Suspend()
    {
        CancellationTokenSource? session;
        lock (stateGate)
        {
            session = readerSession;
            readerSession = null;
        }

        session?.Cancel();
        session?.Dispose();
        lock (deviceOpenGate)
        {
            foreach (var stream in openStreams.ToArray())
            {
                stream.Dispose();
            }

            openStreams.Clear();
        }

        if (session is not null)
        {
            log.Info("Cabinet HID input suspended and released.");
        }
    }

    private async Task RunDeviceLoopAsync(HidDeviceSettings settingsForDevice, CancellationToken cancellationToken)
    {
        if (!TryParseId(settingsForDevice.VendorId, out var vendorId))
        {
            log.Error($"Invalid HID vendor id '{settingsForDevice.VendorId}' for {settingsForDevice.Name}.");
            return;
        }

        var productIds = settingsForDevice.ProductIds
            .Select(value => TryParseId(value, out var id) ? (int?)id : null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToHashSet();
        if (productIds.Count == 0)
        {
            log.Error($"No valid HID product id is configured for {settingsForDevice.Name}.");
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                OpenedHidDevice? opened;
                lock (deviceOpenGate)
                {
                    opened = TryOpenDevice(vendorId, productIds, settingsForDevice);
                    if (opened is not null)
                    {
                        openStreams.Add(opened.Stream);
                    }
                }

                if (opened is null)
                {
                    await Task.Delay(settings.HotPlugIntervalMs, cancellationToken);
                    continue;
                }

                try
                {
                    opened.Stream.ReadTimeout = Math.Max(250, settings.HotPlugIntervalMs);
                    log.Info($"HID connected: {settingsForDevice.Name} ({vendorId:X4}:{opened.Device.ProductID:X4}).");
                    ReadReports(opened.Stream, opened.Device, settingsForDevice, cancellationToken);
                }
                finally
                {
                    lock (deviceOpenGate)
                    {
                        openStreams.Remove(opened.Stream);
                    }

                    opened.Stream.Dispose();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                log.Error($"HID reader failed for {settingsForDevice.Name}.", exception);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                log.Info($"HID disconnected: {settingsForDevice.Name}.");
                await Task.Delay(settings.HotPlugIntervalMs, cancellationToken);
            }
        }
    }

    private static OpenedHidDevice? TryOpenDevice(
        int vendorId,
        IReadOnlySet<int> productIds,
        HidDeviceSettings settings)
    {
        var candidates = DeviceList.Local.GetHidDevices(vendorId)
            .Where(candidate => productIds.Contains(candidate.ProductID))
            .OrderBy(candidate => candidate.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.Profile != HidDeviceProfile.Maimoller)
        {
            var selected = candidates.Skip(settings.DeviceIndex).FirstOrDefault();
            return selected is not null && selected.TryOpen(out var stream)
                ? new OpenedHidDevice(selected, stream)
                : null;
        }

        foreach (var candidate in candidates.Where(candidate =>
                     candidate.DevicePath.Contains("mi_00", StringComparison.OrdinalIgnoreCase)))
        {
            if (!candidate.TryOpen(out var stream))
            {
                continue;
            }

            try
            {
                var feature = new byte[Math.Max(33, candidate.GetMaxFeatureReportLength())];
                feature[0] = 1;
                stream.GetFeature(feature);
                var devicePlayer = feature[4] == 2 ? 2 : 1;
                if (settings.Player is 0 || settings.Player == devicePlayer)
                {
                    return new OpenedHidDevice(candidate, stream);
                }
            }
            catch
            {
                // This can be another interface of the composite device.
            }

            stream.Dispose();
        }

        return null;
    }

    private void ReadReports(
        HidStream stream,
        HidDevice device,
        HidDeviceSettings deviceSettings,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Max(16, device.GetMaxInputReportLength())];
        var previous = new Dictionary<(CabinetInputAction Action, int Player), bool>();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var length = stream.Read(buffer, 0, buffer.Length);
                if (length <= 0)
                {
                    continue;
                }

                var state = Decode(deviceSettings, device.ProductID, buffer.AsSpan(0, length));
                foreach (var button in state)
                {
                    var wasPressed = previous.GetValueOrDefault(button.Key);
                    if (button.Value && !wasPressed)
                    {
                        Pressed?.Invoke(this, new CabinetInputEventArgs(button.Key.Action, button.Key.Player));
                    }

                    previous[button.Key] = button.Value;
                }
            }
            catch (TimeoutException)
            {
                // A short timeout lets cancellation and hot-plug changes be observed.
            }
        }
    }

    private static Dictionary<(CabinetInputAction Action, int Player), bool> Decode(
        HidDeviceSettings settings,
        int productId,
        ReadOnlySpan<byte> report)
    {
        return settings.Profile switch
        {
            HidDeviceProfile.SegaIo4 => DecodeSegaIo4(report, settings.Player),
            HidDeviceProfile.AdxHid => DecodeAdxHid(report, settings.Player is 2 ? 2 : 1),
            HidDeviceProfile.NProDx => DecodeNPro(report, settings.Player is 1 or 2
                ? settings.Player
                : productId == 0x5752 ? 2 : 1),
            HidDeviceProfile.Maimoller => DecodeMaimoller(report, settings.Player is 2 ? 2 : 1),
            _ => []
        };
    }

    private static Dictionary<(CabinetInputAction, int), bool> DecodeSegaIo4(ReadOnlySpan<byte> report, int configuredPlayer)
    {
        var result = new Dictionary<(CabinetInputAction, int), bool>();
        var offset = report.Length >= 64 ? 0 : -1;
        var players = configuredPlayer is 1 or 2 ? [configuredPlayer] : new[] { 1, 2 };
        foreach (var player in players)
        {
            var bank = configuredPlayer is 1 or 2 ? 0 : player - 1;
            var lowIndex = 29 + (bank * 2) + offset;
            var highIndex = lowIndex + 1;
            if (lowIndex < 0 || highIndex >= report.Length)
            {
                continue;
            }

            var low = report[lowIndex];
            var high = report[highIndex];
            result[(CabinetInputAction.Select, player)] = (low & (1 << 1)) != 0;
            result[(CabinetInputAction.Up, player)] = (low & (1 << 2)) == 0;
            result[(CabinetInputAction.Down, player)] = (high & (1 << 7)) == 0;
            result[(CabinetInputAction.Confirm, player)] = (high & (1 << 6)) == 0;
        }

        return result;
    }

    private static Dictionary<(CabinetInputAction, int), bool> DecodeAdxHid(ReadOnlySpan<byte> report, int player)
    {
        var result = new Dictionary<(CabinetInputAction, int), bool>();
        var offset = report.Length >= 14 ? 1 : 0;
        SetIfPresent(result, report, offset + 9, CabinetInputAction.Select, player);
        SetIfPresent(result, report, offset + 4, CabinetInputAction.Up, player);
        SetIfPresent(result, report, offset + 1, CabinetInputAction.Down, player);
        SetIfPresent(result, report, offset + 8, CabinetInputAction.Confirm, player);
        return result;
    }

    private static Dictionary<(CabinetInputAction, int), bool> DecodeNPro(ReadOnlySpan<byte> report, int player)
    {
        var result = new Dictionary<(CabinetInputAction, int), bool>();
        var offset = report.Length >= 9 ? 1 : 0;
        if (offset + 7 >= report.Length)
        {
            return result;
        }

        var buttons = report[offset + 6];
        var auxiliary = report[offset + 7];
        result[(CabinetInputAction.Select, player)] = (auxiliary & (1 << 0)) != 0;
        result[(CabinetInputAction.Up, player)] = (buttons & (1 << 0)) != 0;
        result[(CabinetInputAction.Down, player)] = (buttons & (1 << 3)) != 0;
        result[(CabinetInputAction.Confirm, player)] = (buttons & (1 << 4)) != 0;
        return result;
    }

    private static Dictionary<(CabinetInputAction, int), bool> DecodeMaimoller(
        ReadOnlySpan<byte> report,
        int player)
    {
        var result = new Dictionary<(CabinetInputAction, int), bool>();
        var offset = report.Length >= 8 ? 0 : -1;
        if (offset + 7 >= report.Length)
        {
            return result;
        }

        var buttons = report[offset + 6];
        var systemButtons = report[offset + 7];
        result[(CabinetInputAction.Select, player)] = (systemButtons & (1 << 3)) != 0;
        result[(CabinetInputAction.Up, player)] = (buttons & (1 << 0)) != 0;
        result[(CabinetInputAction.Down, player)] = (buttons & (1 << 3)) != 0;
        result[(CabinetInputAction.Confirm, player)] = (buttons & (1 << 4)) != 0;
        return result;
    }

    private static void SetIfPresent(
        IDictionary<(CabinetInputAction, int), bool> state,
        ReadOnlySpan<byte> report,
        int index,
        CabinetInputAction action,
        int player)
    {
        if (index < report.Length)
        {
            state[(action, player)] = report[index] != 0;
        }
    }

    private static bool TryParseId(string value, out int result)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(normalized[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    public void Dispose()
    {
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        Suspend();
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private sealed record OpenedHidDevice(HidDevice Device, HidStream Stream);
}
