using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace PulseOverlay.Core;

public enum LinkState { Connecting, WaitingForData, Live, SignalLost, Reconnecting, Disconnected }

/// <summary>
/// Keeps one watch connected. It connects, subscribes to heart rate notifications and, whenever the
/// link drops or data stops flowing, tears everything down and retries with back-off until stopped.
/// Huawei's "HR Data Broadcast" can silently stop sending, so silence is treated like a disconnect.
/// </summary>
public sealed class DeviceConnection : IAsyncDisposable
{
    public static readonly TimeSpan SignalLostAfter = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ReconnectAfterSilence = TimeSpan.FromSeconds(20);
    static readonly TimeSpan DropGracePeriod = TimeSpan.FromSeconds(2);
    static readonly int[] BackoffSeconds = [2, 4, 8, 15, 30];

    readonly object _gate = new();
    readonly CancellationTokenSource _cts = new();
    Task? _loop;
    TaskCompletionSource _kick = NewSignal();
    TaskCompletionSource _dropped = NewSignal();
    long _lastPacket; // Environment.TickCount64 of the last measurement, 0 = none yet

    BluetoothLEDevice? _device;
    GattSession? _session;
    GattDeviceService? _service;
    GattCharacteristic? _characteristic;

    public DeviceConnection(string id, string name, ulong address, bool? randomAddress)
    {
        Id = id.ToUpperInvariant();
        Name = name;
        Address = address;
        RandomAddress = randomAddress;
    }

    /// <summary>Stable id of the saved watch (the address it was first saved with).</summary>
    public string Id { get; }
    public string Name { get; }

    /// <summary>
    /// Address currently used to connect. Huawei watches advertise under a different (random)
    /// address in HR broadcast mode, so this can move away from <see cref="Id"/>.
    /// </summary>
    public ulong Address { get; private set; }
    /// <summary>True = random address, false = public, null = unknown (let Windows decide).</summary>
    public bool? RandomAddress { get; private set; }
    public LinkState State { get; private set; } = LinkState.Disconnected;
    /// <summary>Failed attempts since the last time data was flowing.</summary>
    public int Attempt { get; private set; }
    public string? LastError { get; private set; }

    public event Action<DeviceConnection>? StateChanged;
    public event Action<DeviceConnection, HeartRateMeasurement>? Measured;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    /// <summary>Skips the current back-off wait, e.g. because the watch was just seen advertising.</summary>
    public void Kick() => Volatile.Read(ref _kick).TrySetResult();

    /// <summary>Use another address from the next attempt on (the watch reappeared under it).</summary>
    public void Retarget(ulong address, bool randomAddress)
    {
        lock (_gate)
        {
            Address = address;
            RandomAddress = randomAddress;
        }
        Log.Write($"{Name}: retarget → {address:X12} ({(randomAddress ? "random" : "public")})");
        Kick();
    }

    async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                SetState(Attempt == 0 ? LinkState.Connecting : LinkState.Reconnecting);
                if (await TryConnectAsync(ct))
                {
                    await WatchAsync(ct);
                    if (!ct.IsCancellationRequested) Log.Write($"{Name}: rebuilding link ({LastError})");
                }
                await CleanupAsync();
                if (ct.IsCancellationRequested) break;

                Attempt++;
                SetState(LinkState.Reconnecting);
                var kick = NewSignal();
                Volatile.Write(ref _kick, kick);
                var wait = TimeSpan.FromSeconds(BackoffSeconds[Math.Min(Attempt, BackoffSeconds.Length) - 1]);
                await Task.WhenAny(Pause(wait, ct), kick.Task);
            }
        }
        finally
        {
            await CleanupAsync();
            SetState(LinkState.Disconnected);
        }
    }

    async Task<bool> TryConnectAsync(CancellationToken ct)
    {
        string step = "start";
        ulong address;
        bool? random;
        lock (_gate) (address, random) = (Address, RandomAddress);
        Log.Write($"{Name}: attempt {Attempt + 1} → {address:X12} (type: {random switch { true => "random", false => "public", _ => "auto" }})");
        try
        {
            _dropped = NewSignal();

            step = "open device";
            _device = random is { } isRandom
                ? await BluetoothLEDevice.FromBluetoothAddressAsync(address,
                    isRandom ? BluetoothAddressType.Random : BluetoothAddressType.Public).AsTask(ct)
                : await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(ct);
            if (_device is null)
                return Fail(Loc.T("err.notFound"), step);
            Log.Write($"{Name}: device opened, status {_device.ConnectionStatus}, type {_device.BluetoothAddressType}");
            _device.ConnectionStatusChanged += OnConnectionStatusChanged;

            // Ask Windows to keep the link up instead of dropping it when idle.
            // Optional: it fails for some unpaired devices, and the watchdog covers drops anyway.
            step = "gatt session";
            try
            {
                _session = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(ct);
                _session.MaintainConnection = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Write($"{Name}: gatt session skipped ({ex.HResult:X8} {ex.Message.Trim()})");
                _session = null;
            }

            step = "services";
            var services = await _device.GetGattServicesForUuidAsync(
                GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached).AsTask(ct);
            if (services.Status == GattCommunicationStatus.Unreachable)
                return Fail(Loc.T("err.unreachableHr"), step);
            if (services.Status != GattCommunicationStatus.Success)
                return Fail(Loc.F("err.unreachable", Describe(services.Status)), step);
            if (services.Services.Count == 0)
                return Fail(Loc.T("err.noService"), step);
            _service = services.Services[0];

            step = "characteristics";
            var chars = await _service.GetCharacteristicsForUuidAsync(
                GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached).AsTask(ct);
            if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
                return Fail(Loc.T("err.noCharacteristic"), step);
            _characteristic = chars.Characteristics[0];
            _characteristic.ValueChanged += OnValueChanged;

            step = "subscribe";
            var cccd = await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct);
            if (cccd != GattCommunicationStatus.Success)
                return Fail(Loc.F("err.notify", Describe(cccd)), step);

            // Learn the address type so it can be saved and used for the next start
            lock (_gate) RandomAddress = _device.BluetoothAddressType == BluetoothAddressType.Random;
            LastError = null;
            Log.Write($"{Name}: connected and subscribed");
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Write($"{Name}: exception at '{step}': {ex.HResult:X8} {ex.GetType().Name} {ex.Message.Trim()}");
            return Fail(Describe(ex), step);
        }
    }

    /// <summary>Turns the common WinRT Bluetooth errors into something a user can act on.</summary>
    static string Describe(Exception ex) => (uint)ex.HResult switch
    {
        0x80070002 => Loc.T("err.refused"),
        0x8007048F => Loc.T("err.outOfRange"),
        0x80070490 => Loc.T("err.notFoundShort"),
        0x800710DF => Loc.T("err.btOff"),
        0x80000013 => Loc.T("err.closed"),
        _ => ex.Message.Trim().TrimEnd('.')
    };

    /// <summary>Returns when the link should be rebuilt: disconnected, or no data for too long.</summary>
    async Task WatchAsync(CancellationToken ct)
    {
        long connectedAt = Environment.TickCount64;
        Interlocked.Exchange(ref _lastPacket, 0);
        SetState(LinkState.WaitingForData);

        while (!ct.IsCancellationRequested)
        {
            var dropped = _dropped.Task;
            if (await Task.WhenAny(Pause(TimeSpan.FromSeconds(1), ct), dropped) == dropped)
            {
                // Windows sometimes reports a short disconnect and recovers by itself
                _dropped = NewSignal();
                await Pause(DropGracePeriod, ct);
                if (_device?.ConnectionStatus != BluetoothConnectionStatus.Connected)
                {
                    LastError = Loc.T("err.dropped");
                    return;
                }
            }
            if (ct.IsCancellationRequested) return;

            long last = Interlocked.Read(ref _lastPacket);
            long silence = Environment.TickCount64 - (last == 0 ? connectedAt : last);
            if (silence >= ReconnectAfterSilence.TotalMilliseconds)
            {
                LastError = Loc.T(last == 0 ? "err.noData" : "err.dataStopped");
                return;
            }
            if (last != 0 && silence >= SignalLostAfter.TotalMilliseconds)
                SetState(LinkState.SignalLost);
        }
    }

    void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var bytes = new byte[args.CharacteristicValue.Length];
        DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(bytes);
        if (!HeartRateParser.TryParse(bytes, out var measurement)) return;

        Interlocked.Exchange(ref _lastPacket, Environment.TickCount64);
        if (State != LinkState.Live)
        {
            Attempt = 0;
            SetState(LinkState.Live);
        }
        Measured?.Invoke(this, measurement);
    }

    void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            _dropped.TrySetResult();
    }

    async Task CleanupAsync()
    {
        var characteristic = _characteristic;
        _characteristic = null;
        if (characteristic != null)
        {
            characteristic.ValueChanged -= OnValueChanged;
            if (_device?.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                try
                {
                    await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch { /* best effort */ }
            }
        }

        _service?.Dispose();
        _service = null;

        if (_session != null)
        {
            try { _session.MaintainConnection = false; } catch { }
            _session.Dispose();
            _session = null;
        }

        if (_device != null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _device.Dispose();
            _device = null;
        }
    }

    bool Fail(string message, string step)
    {
        Log.Write($"{Name}: failed at '{step}': {message}");
        LastError = message;
        return false;
    }

    void SetState(LinkState state)
    {
        lock (_gate)
        {
            // Reconnecting is re-raised so listeners see the new attempt count
            if (State == state && state != LinkState.Reconnecting) return;
            State = state;
        }
        StateChanged?.Invoke(this);
    }

    static string Describe(GattCommunicationStatus status) => status switch
    {
        GattCommunicationStatus.Unreachable => Loc.T("gatt.unreachable"),
        GattCommunicationStatus.AccessDenied => Loc.T("gatt.denied"),
        GattCommunicationStatus.ProtocolError => Loc.T("gatt.protocol"),
        _ => status.ToString()
    };

    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Task.Delay that completes (instead of throwing) when cancelled.</summary>
    static Task Pause(TimeSpan delay, CancellationToken ct) =>
        Task.Delay(delay, ct).ContinueWith(_ => { }, TaskScheduler.Default);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop != null)
        {
            try { await _loop; } catch { }
        }
        else
        {
            SetState(LinkState.Disconnected);
        }
        _cts.Dispose();
    }
}
