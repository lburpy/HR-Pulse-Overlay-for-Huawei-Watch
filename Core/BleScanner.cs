using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace PulseOverlay.Core;

public sealed record ScanResult(
    string Id, ulong Address, bool RandomAddress, string? Name, short Rssi, bool HasHeartRate);

/// <summary>
/// BLE advertisement watcher with two jobs: the scan the user starts to find watches, and a
/// background scan while a saved watch is reconnecting, so it is retried the moment it reappears.
/// Both are active scans: many watches only send their name in the scan response.
/// </summary>
public sealed class BleScanner : IDisposable
{
    static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(1);

    readonly object _gate = new();
    readonly Dictionary<ulong, (long at, bool named, bool hr)> _lastRaised = [];
    BluetoothLEAdvertisementWatcher? _watcher;

    public event Action<ScanResult>? DeviceSeen;
    public event Action<string>? Failed;

    public void Configure(bool userScan, bool background)
    {
        lock (_gate)
        {
            if (!userScan && !background)
            {
                StopLocked();
                return;
            }
            if (_watcher != null) return;

            try
            {
                var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
                watcher.Received += OnReceived;
                watcher.Stopped += OnStopped;
                watcher.Start();
                _watcher = watcher;
                _lastRaised.Clear();
            }
            catch (Exception ex)
            {
                Failed?.Invoke(Loc.F("scan.err.start", ex.Message));
            }
        }
    }

    void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var name = args.Advertisement.LocalName?.Trim();
        bool named = !string.IsNullOrEmpty(name);
        bool hr = args.Advertisement.ServiceUuids.Contains(GattServiceUuids.HeartRate);

        // Busy rooms produce hundreds of adverts a second; pass each device on about once a second
        lock (_gate)
        {
            long now = Environment.TickCount64;
            if (_lastRaised.TryGetValue(args.BluetoothAddress, out var last)
                && now - last.at < RepeatInterval.TotalMilliseconds
                && (last.named || !named) && (last.hr || !hr))
                return;
            _lastRaised[args.BluetoothAddress] = (now, named || last.named, hr || last.hr);
        }

        DeviceSeen?.Invoke(new ScanResult(
            args.BluetoothAddress.ToString("X12"), args.BluetoothAddress,
            args.BluetoothAddressType == BluetoothAddressType.Random,
            named ? name : null, args.RawSignalStrengthInDBm, hr));
    }

    void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        lock (_gate)
        {
            if (sender != _watcher) return; // we stopped it ourselves
            StopLocked();
        }
        Failed?.Invoke(args.Error switch
        {
            BluetoothError.RadioNotAvailable => Loc.T("scan.err.radio"),
            BluetoothError.DisabledByPolicy or BluetoothError.DisabledByUser => Loc.T("scan.err.policy"),
            BluetoothError.ResourceInUse => Loc.T("scan.err.busy"),
            _ => Loc.F("scan.err.other", args.Error)
        });
    }

    void StopLocked()
    {
        if (_watcher == null) return;
        var watcher = _watcher;
        _watcher = null;
        watcher.Received -= OnReceived;
        watcher.Stopped -= OnStopped;
        try { watcher.Stop(); } catch { }
    }

    public void Dispose()
    {
        lock (_gate) StopLocked();
    }
}
