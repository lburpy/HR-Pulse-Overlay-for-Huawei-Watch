using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using PulseOverlay.Core;

namespace PulseOverlay;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class Palette
{
    public static readonly SolidColorBrush Live = Make(0x22, 0xC5, 0x5E);
    public static readonly SolidColorBrush Working = Make(0x60, 0xA5, 0xFA);
    public static readonly SolidColorBrush Warning = Make(0xF5, 0xA5, 0x24);
    public static readonly SolidColorBrush Off = Make(0x6B, 0x70, 0x80);
    public static readonly SolidColorBrush Error = Make(0xEF, 0x44, 0x44);

    public static SolidColorBrush ForState(string state) => state switch
    {
        "live" => Live,
        "connecting" or "waiting" => Working,
        "lost" or "reconnecting" => Warning,
        _ => Off
    };

    static SolidColorBrush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>A saved watch (or the simulator) in the left-hand list.</summary>
public sealed class DeviceRowVm(string id, string name, bool isSimulated) : Observable
{
    string _name = name;
    bool _isActive;
    string _state = "off";
    int _attempt;
    string? _lastError;
    int _bpm, _min, _max, _avg, _count;

    public string Id { get; } = id;
    public bool IsSimulated { get; } = isSimulated;

    /// <summary>Last address that delivered data (may differ from Id, see WatchIdentity).</summary>
    public ulong Address { get; set; }
    public bool? RandomAddress { get; set; }
    public IEnumerable<ulong> KnownAddresses => IsSimulated ? [] : [Address, Convert.ToUInt64(Id, 16)];

    // Bookkeeping for status-bar messages
    internal bool HadDrop;
    internal LinkState LastLinkState = LinkState.Disconnected;

    public string Name { get => _name; set => Set(ref _name, value); }

    public bool IsActive
    {
        get => _isActive;
        set { if (Set(ref _isActive, value)) RaiseState(); }
    }

    public string State
    {
        get => _state;
        set { if (Set(ref _state, value)) RaiseState(); }
    }

    public int Attempt
    {
        get => _attempt;
        set { if (Set(ref _attempt, value)) Raise(nameof(StateText)); }
    }

    public string? LastError
    {
        get => _lastError;
        set { if (Set(ref _lastError, value)) Raise(nameof(StateText)); }
    }

    public int Bpm
    {
        get => _bpm;
        set { if (Set(ref _bpm, value)) Raise(nameof(BpmText)); }
    }

    public int Min => _min;
    public int Max => _max;
    public int Avg => _avg;
    public int Count => _count;
    public int Scares { get; private set; }
    public int AllTimeMax { get; private set; }

    public string BpmText => State == "live" && Bpm > 0 ? Bpm.ToString() : "--";
    public string ToggleText => Loc.T(IsActive ? "row.disconnect" : "row.connect");
    public Visibility ControlsVisibility => IsSimulated ? Visibility.Collapsed : Visibility.Visible;
    public Brush StateBrush => Palette.ForState(State);

    public string ShortStateText => Loc.T(State switch
    {
        "live" or "waiting" or "connecting" or "lost" or "reconnecting" => "state." + State,
        _ => "state.off"
    });

    public string StateText => State switch
    {
        "live" => IsSimulated ? Loc.T("stateLong.liveSim") : Loc.F("stateLong.live", Id),
        "waiting" => Loc.T("stateLong.waiting"),
        "connecting" => Loc.T("stateLong.connecting"),
        "lost" => Loc.T("stateLong.lost"),
        "reconnecting" => Loc.F("stateLong.reconnecting", Attempt) + (LastError is { } e ? $" · {e}" : ""),
        _ => Loc.T(IsActive ? "stateLong.starting" : "stateLong.off")
    };

    /// <summary>Re-reads every translated text after a language switch.</summary>
    public void RefreshTexts() => RaiseState();

    public void Apply(DeviceView view)
    {
        State = view.State;
        Attempt = view.Attempt;
        Bpm = view.Bpm;
        _min = view.Min; _max = view.Max; _avg = view.Avg; _count = view.Count;
        Scares = view.Scares;
        AllTimeMax = view.AllTimeMax;
    }

    void RaiseState()
    {
        Raise(nameof(StateText));
        Raise(nameof(ShortStateText));
        Raise(nameof(StateBrush));
        Raise(nameof(BpmText));
        Raise(nameof(ToggleText));
    }
}

/// <summary>A device found by the Bluetooth scan.</summary>
public sealed partial class ScanItemVm(string id, ulong address, bool randomAddress, string name, bool hasHeartRate)
    : Observable
{
    string _name = name;
    bool _hasHeartRate = hasHeartRate;
    short _rssi;
    bool _isSaved;

    public string Id { get; } = id;
    public ulong Address { get; } = address;
    public bool RandomAddress { get; } = randomAddress;

    public string Name { get => _name; set => Set(ref _name, value); }

    public bool HasHeartRate
    {
        get => _hasHeartRate;
        set { if (Set(ref _hasHeartRate, value)) Raise(nameof(HeartRateVisibility)); }
    }

    public short Rssi
    {
        get => _rssi;
        set { if (Set(ref _rssi, value)) Raise(nameof(SignalText)); }
    }

    public bool IsSaved
    {
        get => _isSaved;
        set { if (Set(ref _isSaved, value)) Raise(nameof(SavedVisibility)); }
    }

    public Visibility HeartRateVisibility => HasHeartRate ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SavedVisibility => IsSaved ? Visibility.Visible : Visibility.Collapsed;

    public string SignalText => Rssi switch
    {
        >= -60 => "▂▄▆█",
        >= -70 => "▂▄▆",
        >= -82 => "▂▄",
        _ => "▂"
    };

    /// <summary>Hides TVs, phones and the like unless "show all" is ticked.</summary>
    public bool LooksLikeWearable => HasHeartRate || WearableName().IsMatch(Name);

    [GeneratedRegex(@"huawei|honor|watch|band|fit|\bgt\d|polar|garmin|wahoo|coros|amazfit|suunto|heart|\bhr\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex WearableName();
}
