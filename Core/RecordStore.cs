using System.IO;
using System.Text.Json;

namespace PulseOverlay.Core;

/// <summary>
/// All-time records per watch, kept in %APPDATA%\PulseOverlay\records.json (local only).
/// Writes are batched: at most every few seconds, plus once on shutdown.
/// </summary>
public sealed class RecordStore : IDisposable
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public sealed class Entry
    {
        public string Name { get; set; } = "";
        public int MaxBpm { get; set; }
        public DateTimeOffset? MaxAt { get; set; }
        public int TotalScares { get; set; }
    }

    readonly object _lock = new();
    readonly string _path;
    readonly Dictionary<string, Entry> _entries;
    readonly Timer _timer;
    bool _dirty;

    public RecordStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PulseOverlay");
        _path = Path.Combine(folder, "records.json");
        _entries = Load(_path);
        _timer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public int MaxBpm(string id)
    {
        lock (_lock) return _entries.TryGetValue(id, out var e) ? e.MaxBpm : 0;
    }

    public void SetMax(string id, string name, int bpm)
    {
        lock (_lock)
        {
            var entry = Get(id, name);
            entry.MaxBpm = bpm;
            entry.MaxAt = DateTimeOffset.Now;
            _dirty = true;
        }
    }

    public void AddScare(string id, string name)
    {
        lock (_lock)
        {
            Get(id, name).TotalScares++;
            _dirty = true;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _entries.Clear();
            _dirty = true;
        }
        Flush();
    }

    public void Flush()
    {
        string json;
        lock (_lock)
        {
            if (!_dirty) return;
            _dirty = false;
            json = JsonSerializer.Serialize(_entries, Json);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, json);
        }
        catch { /* records are a nicety; never crash over them */ }
    }

    Entry Get(string id, string name)
    {
        if (!_entries.TryGetValue(id, out var entry)) _entries[id] = entry = new Entry();
        if (!string.IsNullOrWhiteSpace(name)) entry.Name = name;
        return entry;
    }

    static Dictionary<string, Entry> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) ?? [];
        }
        catch { /* corrupt file: start fresh */ }
        return [];
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }
}
