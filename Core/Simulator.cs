namespace PulseOverlay.Core;

/// <summary>Fake heart rate source that sweeps through every zone, for testing overlays without a watch.</summary>
public sealed class Simulator : IDisposable
{
    public const string DeviceId = "SIM";
    public static string DeviceName => Loc.T("sim.label");

    readonly object _gate = new();
    readonly HeartRateHub _hub;
    readonly Timer _timer;
    readonly Random _rng = new();
    double _bpm = 74;
    double _spike;
    int _tick;
    int _nextSpike = 40;
    bool _disposed;

    public Simulator(HeartRateHub hub)
    {
        _hub = hub;
        _hub.SetState(DeviceId, DeviceName, "live");
        _timer = new Timer(_ => Tick(), null, 0, 1000);
    }

    void Tick()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _tick++;
            // Slow wave (about 2.5 minutes per cycle) plus a faster wobble and noise
            double target = 115 + 52 * Math.Sin(_tick / 25.0) + 8 * Math.Sin(_tick / 6.0);
            _bpm += Math.Clamp((target - _bpm) * 0.3 + (_rng.NextDouble() * 4 - 2), -7, 7);

            // Every minute or so a sudden fright: +30 BPM within a few seconds, then easing off
            if (_tick >= _nextSpike)
            {
                _spike = Math.Min(32, _spike + 11);
                if (_spike >= 32) _nextSpike = _tick + _rng.Next(50, 90);
            }
            else
            {
                _spike *= 0.9;
            }

            // Fake data: never written to the all-time records
            _hub.Report(DeviceId, DeviceName, (int)Math.Round(_bpm + _spike), persist: false);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer.Dispose();
        _hub.Remove(DeviceId);
    }
}
