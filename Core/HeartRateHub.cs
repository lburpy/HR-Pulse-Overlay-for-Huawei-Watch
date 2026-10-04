using System.Text.Json;

namespace PulseOverlay.Core;

public sealed record DeviceView(
    string Id, string Name, string State, int Attempt, int Bpm, int Min, int Max, int Avg, int Count,
    int Scares, int AllTimeMax);

/// <summary>A scare moment or a new record, for the app's status bar.</summary>
public sealed record HubEvent(string Kind, string Id, string Name, int Bpm, string? Scope, int Value);

/// <summary>
/// Single source of truth for every heart rate source (watches and the simulator). Keeps per-device
/// state, session stats and a short history, detects scare moments and records, and turns every
/// change into a JSON message for overlays.
/// State values sent to overlays: connecting, waiting, live, lost, reconnecting, off.
/// </summary>
public sealed class HeartRateHub
{
    const long HistoryMs = 10 * 60 * 1000;
    const long SnapshotHistoryMs = 5 * 60 * 1000;

    // Scare moment: a jump of ScareRise BPM above the lowest value of the last ScareWindowMs,
    // held for two readings in a row; then quiet for ScareCooldownMs.
    const long ScareWindowMs = 10_000;
    const int ScareRise = 20;
    const long ScareCooldownMs = 30_000;

    // Stream record: only after a warm-up, and only for a real peak (not 75 → 76 at rest)
    const int SessionRecordMinSamples = 120;
    const int SessionRecordAboveAvg = 15;
    const int AllTimeRecordMinSamples = 30;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly object _lock = new();
    readonly Dictionary<string, Track> _tracks = [];
    readonly RecordStore? _records;

    public HeartRateHub(RecordStore? records = null) => _records = records;

    /// <summary>JSON message for overlays. Raised under the hub lock so messages stay in order.</summary>
    public event Action<string>? Message;
    public event Action<DeviceView>? Changed;
    public event Action<HubEvent>? Event;

    public static string ToWire(LinkState state) => state switch
    {
        LinkState.Connecting => "connecting",
        LinkState.WaitingForData => "waiting",
        LinkState.Live => "live",
        LinkState.SignalLost => "lost",
        LinkState.Reconnecting => "reconnecting",
        _ => "off"
    };

    public void SetState(string id, string name, string state, int attempt = 0)
    {
        DeviceView view;
        lock (_lock)
        {
            var track = GetOrAdd(id, name);
            track.State = state;
            track.Attempt = attempt;
            view = View(track);
            Message?.Invoke(Serialize(new { type = "state", id = track.Id, name = track.Name, state, attempt }));
        }
        Changed?.Invoke(view);
    }

    /// <param name="persist">False for fake sources (simulator): no all-time records.</param>
    public void Report(string id, string name, int bpm, bool persist = true)
    {
        DeviceView view;
        var events = new List<HubEvent>();
        lock (_lock)
        {
            var track = GetOrAdd(id, name);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int previousMax = track.Max, previousAvg = track.Avg, previousCount = track.Count;
            track.Add(now, bpm);

            if (DetectScare(track, now, out int rise))
            {
                track.Scares++;
                if (persist) _records?.AddScare(track.Id, track.Name);
                events.Add(new HubEvent("scare", track.Id, track.Name, bpm, null, rise));
            }

            // A new all-time record is announced instead of (and implies) a stream record
            bool allTime = false;
            if (persist && _records != null)
            {
                int stored = _records.MaxBpm(track.Id);
                if (bpm > stored)
                {
                    _records.SetMax(track.Id, track.Name, bpm);
                    // Only celebrate beating a record from an earlier session
                    allTime = track.AllTimeBaseline > 0 && previousCount >= AllTimeRecordMinSamples;
                }
            }
            bool session = previousCount >= SessionRecordMinSamples
                && bpm > previousMax && bpm >= previousAvg + SessionRecordAboveAvg;
            if (allTime || session)
                events.Add(new HubEvent("record", track.Id, track.Name, bpm, allTime ? "alltime" : "session", bpm));

            view = View(track);
            Message?.Invoke(Serialize(new
            {
                type = "hr", id = track.Id, name = track.Name, bpm, t = now,
                min = track.Min, max = track.Max, avg = track.Avg, scares = track.Scares, alltime = view.AllTimeMax
            }));
            foreach (var e in events)
            {
                Message?.Invoke(Serialize(new
                {
                    type = "event", kind = e.Kind, id = e.Id, bpm = e.Bpm, scope = e.Scope,
                    rise = e.Kind == "scare" ? e.Value : 0, count = track.Scares
                }));
            }
        }
        Changed?.Invoke(view);
        foreach (var e in events) Event?.Invoke(e);
    }

    static bool DetectScare(Track track, long now, out int rise)
    {
        rise = 0;
        if (now - track.LastScareAt < ScareCooldownMs) return false;

        var recent = track.History.Where(h => h.T >= now - ScareWindowMs).ToList();
        if (recent.Count < 4) return false;

        int baseline = recent.Take(recent.Count - 2).Min(h => h.Bpm);
        if (recent[^2].Bpm - baseline < ScareRise || recent[^1].Bpm - baseline < ScareRise) return false;

        rise = recent[^1].Bpm - baseline;
        track.LastScareAt = now;
        return true;
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            if (!_tracks.Remove(id)) return;
            Message?.Invoke(Serialize(new { type = "removed", id }));
        }
    }

    /// <summary>Starts a new "stream": session stats, history and scare count.</summary>
    public void ResetStats()
    {
        List<DeviceView> views;
        lock (_lock)
        {
            foreach (var track in _tracks.Values)
            {
                track.ResetStats();
                track.AllTimeBaseline = _records?.MaxBpm(track.Id) ?? 0;
            }
            views = _tracks.Values.Select(View).ToList();
            Message?.Invoke(SnapshotLocked());
        }
        foreach (var view in views) Changed?.Invoke(view);
    }

    /// <summary>Forgets all-time records; the current session then sets the new ones quietly.</summary>
    public void ResetRecords()
    {
        List<DeviceView> views;
        lock (_lock)
        {
            _records?.Reset();
            foreach (var track in _tracks.Values) track.AllTimeBaseline = 0;
            views = _tracks.Values.Select(View).ToList();
            Message?.Invoke(SnapshotLocked());
        }
        foreach (var view in views) Changed?.Invoke(view);
    }

    public string SnapshotJson()
    {
        lock (_lock) return SnapshotLocked();
    }

    string SnapshotLocked()
    {
        long since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - SnapshotHistoryMs;
        return Serialize(new
        {
            type = "snapshot",
            devices = _tracks.Values.Select(t => new
            {
                id = t.Id, name = t.Name, state = t.State, attempt = t.Attempt, bpm = t.Bpm,
                min = t.Min, max = t.Max, avg = t.Avg, updated = t.Updated,
                scares = t.Scares, alltime = _records?.MaxBpm(t.Id) ?? 0,
                history = t.History.Where(h => h.T >= since).Select(h => new[] { h.T, h.Bpm })
            })
        });
    }

    Track GetOrAdd(string id, string name)
    {
        if (!_tracks.TryGetValue(id, out var track))
        {
            _tracks[id] = track = new Track { Id = id, AllTimeBaseline = _records?.MaxBpm(id) ?? 0 };
        }
        if (!string.IsNullOrWhiteSpace(name)) track.Name = name;
        return track;
    }

    DeviceView View(Track t) => new(t.Id, t.Name, t.State, t.Attempt, t.Bpm, t.Min, t.Max, t.Avg, t.Count,
        t.Scares, _records?.MaxBpm(t.Id) ?? 0);

    static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    sealed class Track
    {
        public string Id = "";
        public string Name = "";
        public string State = "off";
        public int Attempt;
        public int Bpm;
        public long Updated;
        public int Min, Max, Count;
        public int Scares;
        public long LastScareAt;
        /// <summary>All-time record when this session started; 0 = no earlier record.</summary>
        public int AllTimeBaseline;
        long _sum;
        public readonly Queue<(long T, int Bpm)> History = new();

        public int Avg => Count == 0 ? 0 : (int)Math.Round((double)_sum / Count);

        public void Add(long now, int bpm)
        {
            Bpm = bpm;
            Updated = now;
            Min = Count == 0 ? bpm : Math.Min(Min, bpm);
            Max = Math.Max(Max, bpm);
            _sum += bpm;
            Count++;
            History.Enqueue((now, bpm));
            while (History.Count > 0 && now - History.Peek().T > HistoryMs) History.Dequeue();
        }

        public void ResetStats()
        {
            Min = Max = Count = Scares = 0;
            _sum = 0;
            LastScareAt = 0;
            History.Clear();
        }
    }
}
