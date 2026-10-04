using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PulseOverlay.Core;
using PulseOverlay.Server;

namespace PulseOverlay;

public partial class MainWindow : Window
{
    readonly AppSettings _settings = AppSettings.Load();
    readonly RecordStore _records = new();
    readonly HeartRateHub _hub;
    readonly BleScanner _scanner = new();
    readonly Dictionary<string, DeviceConnection> _connections = [];
    readonly Dictionary<string, long> _retargetedAt = [];
    readonly Dictionary<ulong, long> _lastSeen = [];
    readonly ObservableCollection<DeviceRowVm> _rows = [];
    readonly ObservableCollection<ScanItemVm> _scanItems = [];
    readonly ICollectionView _scanView;
    readonly DispatcherTimer _heartTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    OverlayServer? _server;
    Simulator? _simulator;
    bool _userScanning;
    bool _shutdownDone;
    int _heroBpm;
    long _nextHeroBeat;

    public MainWindow()
    {
        _hub = new HeartRateHub(_records);
        InitializeComponent();
        DeviceRows.ItemsSource = _rows;

        _scanView = CollectionViewSource.GetDefaultView(_scanItems);
        _scanView.SortDescriptions.Add(new SortDescription(nameof(ScanItemVm.HasHeartRate), ListSortDirection.Descending));
        _scanView.SortDescriptions.Add(new SortDescription(nameof(ScanItemVm.Name), ListSortDirection.Ascending));
        _scanView.Filter = o =>
        {
            var item = (ScanItemVm)o;
            if (ShowAllDevices.IsChecked == true) return true;
            // Hide a watch's phone identity when its HR identity is listed too: only that one works
            if (!item.HasHeartRate && _scanItems.Any(other => other.HasHeartRate
                    && (other.Address & 0xFFFFFFFF) == (item.Address & 0xFFFFFFFF)))
                return false;
            return item.LooksLikeWearable;
        };
        ScanList.ItemsSource = _scanView;

        LanguageBox.SelectedIndex = Loc.Language == "en" ? 1 : 0;
        _heartTimer.Tick += (_, _) => HeroHeartTick();
        SourceInitialized += (_, _) => UseDarkTitleBar();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    // ── Startup ───────────────────────────────────────────────────────────

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartServer();

        _hub.Changed += view => Dispatcher.BeginInvoke(() => OnHubChanged(view));
        _hub.Event += ev => Dispatcher.BeginInvoke(() => OnHubEvent(ev));
        _scanner.DeviceSeen += result => Dispatcher.BeginInvoke(() => OnDeviceSeen(result));
        _scanner.Failed += message => Dispatcher.BeginInvoke(() => OnScannerFailed(message));

        // Saved watches are listed but never connected on startup; the user presses Connect
        foreach (var saved in _settings.Devices)
        {
            var row = AddRow(saved.Id, saved.Name);
            row.Address = Convert.ToUInt64(string.IsNullOrEmpty(saved.Address) ? saved.Id : saved.Address, 16);
            row.RandomAddress = saved.RandomAddress;
        }

        SetStatus(Loc.T(_rows.Count > 0 ? "status.startSaved" : "status.startNew"));
        RefreshTexts();
    }

    void StartServer()
    {
        try
        {
            _server = new OverlayServer(_hub, _settings.Port);
            _server.ClientCountChanged += count => Dispatcher.BeginInvoke(() => ObsClients.Text = count.ToString());
            _server.Start();
            UrlBox.Text = _server.BaseUrl;
            ServerText.Text = Loc.F("server.ready", _settings.Port);
            ServerDot.Fill = Palette.Live;
        }
        catch (Exception ex)
        {
            _server = null;
            UrlBox.Text = "—";
            ServerText.Text = Loc.T("server.failed");
            ServerDot.Fill = Palette.Error;
            SetStatus(Loc.F("server.portBusy", _settings.Port, ex.Message));
        }
    }

    // ── Connections ───────────────────────────────────────────────────────

    DeviceRowVm AddRow(string id, string name, bool simulated = false)
    {
        var row = new DeviceRowVm(id, name, simulated);
        _rows.Add(row);
        RefreshEmptyStates();
        return row;
    }

    void StartConnection(DeviceRowVm row)
    {
        if (_connections.ContainsKey(row.Id)) return;

        var connection = new DeviceConnection(row.Id, row.Name, row.Address, row.RandomAddress);
        connection.StateChanged += c =>
        {
            // Snapshot now: the connection keeps changing on its own thread
            var (state, attempt, error) = (c.State, c.Attempt, c.LastError);
            _hub.SetState(c.Id, c.Name, HeartRateHub.ToWire(state), attempt);
            Dispatcher.BeginInvoke(() => OnConnectionState(c, state, attempt, error));
        };
        connection.Measured += (c, m) => _hub.Report(c.Id, c.Name, m.Bpm);

        _connections[row.Id] = connection;
        row.IsActive = true;
        connection.Start();
        UpdateWatcher();
    }

    async Task StopConnectionAsync(DeviceRowVm row)
    {
        if (!_connections.Remove(row.Id, out var connection)) return;
        row.IsActive = false;
        UpdateWatcher();
        await connection.DisposeAsync();
    }

    void OnConnectionState(DeviceConnection connection, LinkState state, int attempt, string? error)
    {
        var row = FindRow(connection.Id);
        if (row == null || !_connections.ContainsKey(connection.Id)) return;

        row.LastError = error;
        var previous = row.LastLinkState;
        row.LastLinkState = state;

        // Remember the address that actually delivers data, so the next start goes straight to it
        if (state == LinkState.Live
            && (row.Address != connection.Address || row.RandomAddress != connection.RandomAddress))
        {
            row.Address = connection.Address;
            row.RandomAddress = connection.RandomAddress;
            SaveDevices();
        }

        switch (state)
        {
            case LinkState.Live when row.HadDrop:
                row.HadDrop = false;
                SetStatus(Loc.F("status.reconnected", row.Name));
                break;
            case LinkState.Live when previous != LinkState.Live && previous != LinkState.SignalLost:
                SetStatus(Loc.F("status.live", row.Name));
                break;
            case LinkState.Live when previous == LinkState.SignalLost:
                SetStatus(Loc.F("status.signalBack", row.Name));
                break;
            case LinkState.SignalLost:
                SetStatus(Loc.F("status.signalLost", row.Name));
                break;
            case LinkState.WaitingForData:
                SetStatus(Loc.F("status.waiting", row.Name));
                break;
            case LinkState.Reconnecting when attempt == 1:
                row.HadDrop = true;
                SetStatus(Loc.F("status.dropped", row.Name, error ?? Loc.T("status.droppedDefault")));
                break;
        }
        UpdateWatcher();
    }

    /// <summary>Listen for adverts while a watch is reconnecting, so it is retried as soon as it reappears.</summary>
    void UpdateWatcher()
    {
        bool reconnecting = _connections.Values.Any(c => c.State == LinkState.Reconnecting);
        _scanner.Configure(_userScanning, reconnecting);
    }

    void OnHubChanged(DeviceView view)
    {
        var row = FindRow(view.Id);
        if (row == null) return;
        row.Apply(view);
        RefreshHero();
    }

    void OnHubEvent(HubEvent ev)
    {
        SetStatus(ev switch
        {
            { Kind: "scare" } => Loc.F("status.scare", ev.Name, ev.Value, ev.Bpm),
            { Kind: "record", Scope: "alltime" } => Loc.F("status.recordAll", ev.Name, ev.Bpm),
            _ => Loc.F("status.recordSession", ev.Name, ev.Bpm)
        });
    }

    // ── Scanning ──────────────────────────────────────────────────────────

    void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        SetUserScanning(!_userScanning);
    }

    void SetUserScanning(bool on)
    {
        _userScanning = on;
        if (on) _scanItems.Clear();
        UpdateWatcher();
        RefreshScanTexts();
        RefreshEmptyStates();
    }

    void OnDeviceSeen(ScanResult result)
    {
        FollowReconnectingWatch(result);

        if (!_userScanning || (result.Name == null && !result.HasHeartRate)) return;

        var item = _scanItems.FirstOrDefault(i => i.Id == result.Id);
        if (item == null)
        {
            item = new ScanItemVm(result.Id, result.Address, result.RandomAddress,
                result.Name ?? Loc.F("scan.hrDevice", result.Id), result.HasHeartRate)
            {
                Rssi = result.Rssi,
                IsSaved = FindRowFor(result.Address, result.Name) != null
            };
            _scanItems.Add(item);
            if (item.HasHeartRate) _scanView.Refresh(); // may hide this watch's phone identity
            RefreshEmptyStates();
        }
        else
        {
            item.Rssi = result.Rssi;
            if (result.Name != null && item.Name != result.Name) item.Name = result.Name;
            if (result.HasHeartRate && !item.HasHeartRate)
            {
                item.HasHeartRate = true;
                _scanView.Refresh();
            }
        }
    }

    /// <summary>
    /// A reconnecting watch just showed up: retry now instead of waiting out the back-off, and if it
    /// came back under a new address (Huawei HR broadcast mode), switch the connection to it.
    /// </summary>
    void FollowReconnectingWatch(ScanResult result)
    {
        // Adverts arrive every second; only a watch that was gone for a while counts as "back".
        // Kicking on every advert would start a new attempt each second and starve the scan.
        long now = Environment.TickCount64;
        bool reappeared = !_lastSeen.TryGetValue(result.Address, out long seen) || now - seen > 10_000;
        _lastSeen[result.Address] = now;

        foreach (var row in _rows)
        {
            if (!_connections.TryGetValue(row.Id, out var connection) || connection.State != LinkState.Reconnecting)
                continue;
            if (!WatchIdentity.IsSameWatch(row.Name, [.. row.KnownAddresses, connection.Address], result.Address, result.Name))
                continue;

            if (reappeared)
                Log.Write($"{row.Name}: seen {result.Id} “{result.Name}” hr={result.HasHeartRate} rssi={result.Rssi}");

            if (result.Address == connection.Address)
            {
                if (reappeared) connection.Kick();
                continue;
            }

            // Huawei keeps advertising its normal (phone) identity next to the HR one, and only the
            // HR one serves heart rate to the PC: never switch to an identity without the HR service
            if (!result.HasHeartRate) continue;

            // Two addresses of the same watch can both be advertising; don't flip-flop between them
            string key = $"{row.Id}:{result.Id}";
            if (_retargetedAt.TryGetValue(key, out long at) && now - at < 20_000) continue;
            _retargetedAt[key] = now;

            connection.Retarget(result.Address, result.RandomAddress);
            SetStatus(Loc.F("status.seenAs", row.Name, result.Name ?? result.Id));
        }
    }

    DeviceRowVm? FindRowFor(ulong address, string? name) =>
        _rows.FirstOrDefault(r => !r.IsSimulated && WatchIdentity.IsSameWatch(r.Name, r.KnownAddresses, address, name));

    void OnScannerFailed(string message)
    {
        if (_userScanning) SetUserScanning(false);
        SetStatus(message);
    }

    void ShowAllDevices_Changed(object sender, RoutedEventArgs e)
    {
        _scanView.Refresh();
        RefreshEmptyStates();
    }

    void ScanList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ScanList.SelectedItem is not ScanItemVm item)
        {
            ConnectButton.IsEnabled = false;
            return;
        }
        var row = FindRowFor(item.Address, item.Name);
        ConnectButton.IsEnabled = row == null
            || !_connections.TryGetValue(row.Id, out var connection)
            || connection.State != LinkState.Live;
    }

    void ScanList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ScanList.SelectedItem is ScanItemVm) ConnectSelected_Click(sender, e);
    }

    void ConnectSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ScanList.SelectedItem is not ScanItemVm item) return;

        // The same watch may already be saved under its other (non-broadcast) address
        var row = FindRowFor(item.Address, item.Name) ?? AddRow(item.Id, item.Name);
        row.Address = item.Address;
        row.RandomAddress = item.RandomAddress;
        item.IsSaved = true;
        SaveDevices();

        // Scanning while connecting makes many adapters slower and flakier
        if (_userScanning) SetUserScanning(false);
        if (_connections.TryGetValue(row.Id, out var running))
            running.Retarget(item.Address, item.RandomAddress);
        else
            StartConnection(row);
        ConnectButton.IsEnabled = false;
        SetStatus(Loc.F("status.connecting", item.Name));
    }

    // ── Saved watch rows ──────────────────────────────────────────────────

    async void ToggleConnection_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceRowVm row) return;
        if (row.IsActive)
        {
            SetStatus(Loc.F("status.disconnecting", row.Name));
            await StopConnectionAsync(row);
            SetStatus(Loc.F("status.disconnected", row.Name));
        }
        else
        {
            StartConnection(row);
        }
    }

    void CopyDeviceUrl_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceRowVm row || _server == null) return;
        CopyToClipboard($"{_server.BaseUrl}overlay/{row.Id}", Loc.F("status.urlCopiedDevice", row.Name));
    }

    async void Forget_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceRowVm row) return;
        var answer = MessageBox.Show(this, Loc.F("confirm.forget", row.Name), "Pulse Overlay",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        _rows.Remove(row);
        SaveDevices();
        RefreshEmptyStates();
        foreach (var item in _scanItems) item.IsSaved = FindRowFor(item.Address, item.Name) != null;
        await StopConnectionAsync(row);
        _hub.Remove(row.Id);
        RefreshHero();
        SetStatus(Loc.F("status.forgotten", row.Name));
    }

    void SaveDevices()
    {
        _settings.Devices = _rows
            .Where(r => !r.IsSimulated)
            .Select(r => new SavedDevice
            {
                Id = r.Id, Name = r.Name,
                Address = r.Address.ToString("X12"), RandomAddress = r.RandomAddress
            })
            .ToList();
        _settings.Save();
    }

    // ── Simulator ─────────────────────────────────────────────────────────

    void SimToggle_Checked(object sender, RoutedEventArgs e)
    {
        var row = AddRow(Simulator.DeviceId, Simulator.DeviceName, simulated: true);
        row.IsActive = true;
        _simulator = new Simulator(_hub);
        SetStatus(Loc.T("status.simOn"));
    }

    void SimToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _simulator?.Dispose();
        _simulator = null;
        var row = FindRow(Simulator.DeviceId);
        if (row != null) _rows.Remove(row);
        RefreshEmptyStates();
        RefreshHero();
        SetStatus(Loc.T("status.simOff"));
    }

    void ResetStats_Click(object sender, RoutedEventArgs e)
    {
        _hub.ResetStats();
        SetStatus(Loc.T("status.newStream"));
    }

    void ResetRecords_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.T("confirm.resetRecord"), "Pulse Overlay",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        _hub.ResetRecords();
        SetStatus(Loc.T("status.recordReset"));
    }

    // ── Overlay ───────────────────────────────────────────────────────────

    void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (_server != null) CopyToClipboard(_server.BaseUrl, Loc.T("status.urlCopied"));
    }

    void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_server != null) OpenInBrowser($"{_server.BaseUrl}editor?ui={Loc.Language}");
    }

    void OpenOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_server != null) OpenInBrowser(_server.BaseUrl);
    }

    void OpenInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus(Loc.F("status.browserFailed", ex.Message)); }
    }

    void CopyToClipboard(string text, string message)
    {
        try
        {
            Clipboard.SetText(text);
            SetStatus(message);
        }
        catch
        {
            SetStatus(Loc.T("status.clipboardBusy"));
        }
    }

    // ── Hero (big live number) ────────────────────────────────────────────

    void RefreshHero()
    {
        var primary = _rows.FirstOrDefault(r => r.State == "live") ?? _rows.FirstOrDefault(r => r.IsActive);
        if (primary == null)
        {
            HeroName.Text = Loc.T(_rows.Count == 0 ? "hero.noWatch" : "hero.notConnected");
            HeroBpm.Text = "--";
            HeroStats.Text = " ";
            HeroRecords.Text = " ";
            SetHeroChip(Loc.T("state.off"), Palette.Off);
            SetHeroBpm(0);
            return;
        }

        bool live = primary.State == "live" && primary.Bpm > 0;
        HeroName.Text = primary.Name.ToUpperInvariant();
        HeroBpm.Text = live ? primary.Bpm.ToString() : "--";
        HeroStats.Text = primary.Count > 0
            ? Loc.F("hero.stats", primary.Min, primary.Avg, primary.Max)
            : " ";
        HeroRecords.Text = primary.IsSimulated
            ? Loc.F("hero.recordsSim", primary.Scares)
            : Loc.F("hero.records", primary.Scares, primary.AllTimeMax > 0 ? primary.AllTimeMax : "—");
        SetHeroChip(primary.ShortStateText, primary.StateBrush);
        SetHeroBpm(live ? primary.Bpm : 0);
    }

    void SetHeroChip(string text, Brush brush)
    {
        HeroState.Text = text;
        HeroState.Foreground = brush;
        HeroDot.Fill = brush;
    }

    void SetHeroBpm(int bpm)
    {
        _heroBpm = bpm;
        HeroHeart.Opacity = bpm > 0 ? 1 : 0.35;
        if (bpm > 0 && !_heartTimer.IsEnabled)
        {
            _nextHeroBeat = Environment.TickCount64;
            _heartTimer.Start();
        }
        else if (bpm == 0)
        {
            _heartTimer.Stop();
        }
    }

    void HeroHeartTick()
    {
        long now = Environment.TickCount64;
        if (_heroBpm <= 0 || now < _nextHeroBeat) return;
        _nextHeroBeat = now + 60000 / _heroBpm;

        var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(215))));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        HeroHeartScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        HeroHeartScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    // ── Language ──────────────────────────────────────────────────────────

    void LanguageBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LanguageBox.SelectedItem is not System.Windows.Controls.ComboBoxItem { Tag: string language }
            || language == Loc.Language)
            return;
        Loc.Set(language);
        _settings.Language = language;
        _settings.Save();
        RefreshTexts();
        SetStatus(Loc.T("status.langChanged"));
    }

    /// <summary>Text set from code (XAML-bound text follows the language by itself).</summary>
    void RefreshTexts()
    {
        if (_server != null) ServerText.Text = Loc.F("server.ready", _settings.Port);
        foreach (var row in _rows)
        {
            if (row.IsSimulated) row.Name = Simulator.DeviceName;
            row.RefreshTexts();
        }
        RefreshScanTexts();
        RefreshHero();
        RefreshEmptyStates();
    }

    void RefreshScanTexts()
    {
        ScanButton.Content = Loc.T(_userScanning ? "scan.stop" : "scan.scan");
        ScanHint.Text = Loc.T(_userScanning ? "scan.hintScanning" : "scan.hint");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    DeviceRowVm? FindRow(string id) => _rows.FirstOrDefault(r => r.Id == id);

    void RefreshEmptyStates()
    {
        EmptyRows.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool anyVisible = !_scanView.IsEmpty;
        ScanEmpty.Visibility = anyVisible ? Visibility.Collapsed : Visibility.Visible;
        ScanEmpty.Text = Loc.T(_userScanning
            ? (_scanItems.Count > 0 ? "scan.noWearable" : "scan.searching")
            : "scan.emptyIdle");
    }

    void SetStatus(string message) => StatusText.Text = message;

    void UseDarkTitleBar()
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        int on = 1;
        var hwnd = new WindowInteropHelper(this).Handle;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ── Shutdown ──────────────────────────────────────────────────────────

    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownDone) return;
        e.Cancel = true;
        IsEnabled = false;
        SetStatus(Loc.T("status.closing"));

        // Don't let a stuck Bluetooth call keep the window open forever
        await Task.WhenAny(ShutdownAsync(), Task.Delay(TimeSpan.FromSeconds(4)));
        _shutdownDone = true;
        Close();
    }

    async Task ShutdownAsync()
    {
        _heartTimer.Stop();
        _scanner.Dispose();
        _simulator?.Dispose();
        _records.Dispose(); // writes any pending record to disk
        await Task.WhenAll(_connections.Values.Select(c => c.DisposeAsync().AsTask()));
        if (_server != null) await _server.DisposeAsync();
    }
}
