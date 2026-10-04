using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using PulseOverlay.Core;

namespace PulseOverlay.Server;

/// <summary>
/// Local web server on a single port:
///   GET /               overlay (most recently active watch)
///   GET /overlay/{ID}   overlay locked to one watch (also ?device=ID)
///   GET /editor         overlay editor
///   GET /api/state      JSON snapshot of all devices
///   WS  /ws             live updates (snapshot first, then hr/state/removed messages)
/// Set PULSEOVERLAY_WWWROOT to a folder to serve web files from disk while developing.
/// </summary>
public sealed partial class OverlayServer : IAsyncDisposable
{
    readonly HttpListener _listener = new();
    readonly HeartRateHub _hub;
    readonly ConcurrentDictionary<Client, byte> _clients = new();
    readonly ConcurrentDictionary<string, byte[]?> _embedded = new();
    readonly CancellationTokenSource _cts = new();
    readonly string? _devRoot = Environment.GetEnvironmentVariable("PULSEOVERLAY_WWWROOT");
    Task? _acceptLoop;

    public OverlayServer(HeartRateHub hub, int port)
    {
        _hub = hub;
        Port = port;
    }

    public int Port { get; }
    public string BaseUrl => $"http://localhost:{Port}/";
    public int ClientCount => _clients.Count;

    public event Action<int>? ClientCountChanged;

    public void Start()
    {
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _hub.Message += Broadcast;
        _acceptLoop = AcceptLoopAsync();
    }

    void Broadcast(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        foreach (var client in _clients.Keys) client.Outbox.Writer.TryWrite(bytes);
    }

    async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = (ctx.Request.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();

            if (path == "/ws")
            {
                if (ctx.Request.IsWebSocketRequest) await ServeSocketAsync(ctx);
                else Write(ctx, 400, "text/plain; charset=utf-8", "WebSocket bekleniyordu"u8.ToArray());
                return;
            }
            if (path == "/api/state")
            {
                Write(ctx, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(_hub.SnapshotJson()));
                return;
            }

            var file = path switch
            {
                "" or "/overlay" => "overlay.html",
                "/editor" => "editor.html",
                _ when path.StartsWith("/overlay/") => "overlay.html",
                _ => path.TrimStart('/')
            };
            var content = SafeFileName().IsMatch(file) ? ReadAsset(file) : null;
            if (content == null)
                Write(ctx, 404, "text/plain; charset=utf-8", "Bulunamadı"u8.ToArray());
            else
                Write(ctx, 200, ContentType(file), content);
        }
        catch { /* client went away */ }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    async Task ServeSocketAsync(HttpListenerContext ctx)
    {
        HttpListenerWebSocketContext wsCtx;
        try { wsCtx = await ctx.AcceptWebSocketAsync(null, TimeSpan.FromSeconds(15)); }
        catch { return; }

        var client = new Client(wsCtx.WebSocket);
        // Register before taking the snapshot so no update can slip between the two
        _clients.TryAdd(client, 0);
        client.Outbox.Writer.TryWrite(Encoding.UTF8.GetBytes(_hub.SnapshotJson()));
        ClientCountChanged?.Invoke(_clients.Count);

        try
        {
            await Task.WhenAny(SendLoopAsync(client, _cts.Token), ReceiveLoopAsync(client.Socket, _cts.Token));
        }
        finally
        {
            _clients.TryRemove(client, out _);
            client.Outbox.Writer.TryComplete();
            ClientCountChanged?.Invoke(_clients.Count);
            try
            {
                if (client.Socket.State == WebSocketState.Open)
                    await client.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch { }
            client.Socket.Dispose();
        }
    }

    static async Task SendLoopAsync(Client client, CancellationToken ct)
    {
        try
        {
            await foreach (var message in client.Outbox.Reader.ReadAllAsync(ct))
                await client.Socket.SendAsync(message, WebSocketMessageType.Text, true, ct);
        }
        catch { }
    }

    static async Task ReceiveLoopAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[512];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch { }
    }

    byte[]? ReadAsset(string file)
    {
        if (_devRoot != null)
        {
            var onDisk = Path.Combine(_devRoot, file);
            if (File.Exists(onDisk)) return File.ReadAllBytes(onDisk);
        }
        return _embedded.GetOrAdd(file, name =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("wwwroot/" + name);
            if (stream == null) return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        });
    }

    static void Write(HttpListenerContext ctx, int status, string contentType, byte[] body)
    {
        var response = ctx.Response;
        response.StatusCode = status;
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
    }

    static string ContentType(string file) => Path.GetExtension(file) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".json" => "application/json; charset=utf-8",
        _ => "application/octet-stream"
    };

    [GeneratedRegex(@"^[a-z0-9_-]+\.[a-z0-9]+$")]
    private static partial Regex SafeFileName();

    public async ValueTask DisposeAsync()
    {
        _hub.Message -= Broadcast;
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        if (_acceptLoop != null)
        {
            try { await _acceptLoop; } catch { }
        }
        foreach (var client in _clients.Keys)
        {
            try { client.Socket.Abort(); } catch { }
        }
        try { _listener.Close(); } catch { }
    }

    sealed class Client(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;

        // A slow overlay drops its oldest messages instead of holding up everyone else
        public Channel<byte[]> Outbox { get; } = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    }
}
