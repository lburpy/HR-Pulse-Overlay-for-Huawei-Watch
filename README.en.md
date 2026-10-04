# Pulse Overlay

[Türkçe](README.md) · **English**

A Windows app that reads live heart rate from a Huawei watch (Watch Fit 5, or any device with the
standard BLE heart rate service) and shows it as a customizable overlay in OBS.

## Features

**Self-healing connection**
- If the link drops or the watch stops sending data (Huawei's HR broadcast sometimes stops silently),
  the app reconnects automatically: every 2 → 4 → 8 → 15 → 30 seconds, until you stop it.
- As soon as the watch shows up again, it skips the wait and
  connects right away.
- No data for 5 seconds shows "No signal"; after 20 seconds the connection is rebuilt.
- Watches are remembered and listed on startup; they connect when you press "Connect"
  (the app does not connect by itself on startup).
- Huawei watches use a separate address in HR broadcast mode; after a drop the app recognizes the
  watch under that address too and reconnects.
- The overlay shows every state: Live / No signal / Reconnecting / Not connected / App not running.
- Connection steps and errors are written to `%APPDATA%\PulseOverlay\log.txt`.

**Scare moments & records**
- When heart rate jumps 20+ within ~10 seconds: a "😱 Scared!" effect and a scare counter for the stream.
- Gold badge for a stream record (after the first 2 minutes, 15 above the average) and for an all-time
  record. The all-time record is stored only on this computer, in `%APPDATA%\PulseOverlay\records.json`;
  "Reset record" deletes it. "Start new stream" resets the stream record and the counter.

**Overlay**
- 5 themes: Card, Minimal (on top of gameplay), Neon, Gauge (circular), ECG monitor
- Live heart rate graph (30 s – 5 min), min / avg / max, 5-zone heart rate bar
- Color follows the heart rate zone or stays fixed; font, size, background, corners, alignment
- The heart beats in sync with your pulse; numbers change smoothly
- Alert threshold: flash and/or shake when heart rate goes above X
- Optionally hides itself while there is no signal
- Works offline (system fonts, SVG heart)

**Language**
- The app, the overlay editor and the overlay are available in Turkish and English. Switch it from the
  list at the top right of the app or with the TR / EN buttons in the editor. On first start the
  Windows language is used.

## Usage

1. On the watch: **Settings → HR data broadcasts → On**.
2. In the app: **Scan** → pick your watch → **Connect**.
3. **Customize overlay** → the editor opens in your browser. Pick a theme and settings, then **Copy URL**.
4. In OBS: Sources → **+** → **Browser** → paste the URL and set width/height to the size the editor suggests.

To try it without a watch, turn on the **Simulator** in the app or use **Demo data** in the editor.

## Endpoints

| URL | What it serves |
|-----|----------------|
| `http://localhost:8790/` | Overlay (most recently active watch) |
| `http://localhost:8790/overlay/<ID>` | Overlay for one watch only |
| `http://localhost:8790/editor` | Overlay editor |
| `http://localhost:8790/api/state` | Current state of all devices (JSON) |
| `ws://localhost:8790/ws` | Live data stream |

Overlay options are URL parameters (`theme`, `color`, `accent`, `graph`, `stats`, `zonebar`,
`maxhr`, `alert`, `hideoff`, `lang` …); the editor builds them for you.
Settings are stored in `%APPDATA%\PulseOverlay\settings.json`.

## Building

Requires the .NET 10 SDK.

```powershell
dotnet run

# Single-file EXE: publish\PulseOverlay.exe (~68 MB, no .NET install needed)
dotnet publish -c Release -o publish
```

To try changes to the overlay/editor files without rebuilding, set the `PULSEOVERLAY_WWWROOT`
environment variable to the `wwwroot` folder; the server then reads the files from disk.

## Structure

```
Core/
  DeviceConnection.cs   One watch connection: connect, listen, retry after drops
  BleScanner.cs         Scanning + watching for the watch in the background while reconnecting
  WatchIdentity.cs      Recognizes a watch under another address/name (Huawei HR broadcast)
  HeartRateParser.cs    Decodes the Bluetooth heart rate packet (0x2A37)
  HeartRateHub.cs       State, stats, history, scare moments and records → overlay messages
  RecordStore.cs        All-time records (records.json)
  Simulator.cs          Fake heart rate source
  AppSettings.cs        Saved watches and language
  Log.cs                Diagnostic log (log.txt)
Server/OverlayServer.cs Single port: overlay, editor, JSON API, WebSocket
wwwroot/                overlay.html/css/js, editor.html/css/js (embedded in the EXE)
Loc.cs                  App strings (Turkish / English)
MainWindow.xaml(.cs)    Control panel
```
