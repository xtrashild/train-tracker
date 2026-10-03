using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Api;

/// <summary>Keeps track of connected WebSocket clients and pushes position updates to them.
///
/// Protocol (server to client, JSON text frames):
///   {"type":"snapshot","vehicles":[ ...all latest positions... ]}   once, right after connecting
///   {"type":"position","vehicle":{ ...one position... }}            for every update
///
/// Each client has its own bounded outbox and a dedicated send loop. This gives us:
///   - only one send at a time per socket (WebSocket does not allow concurrent sends),
///   - a slow client never blocks the MQTT thread or other clients,
///   - if a client falls too far behind, its oldest queued messages are dropped.</summary>
public sealed class WebSocketHub
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private readonly VehicleStore _store;
    private readonly ILogger<WebSocketHub> _logger;

    public WebSocketHub(VehicleStore store, ILogger<WebSocketHub> logger)
    {
        _store = store;
        _logger = logger;
        _store.PositionUpdated += OnPositionUpdated;
    }

    /// <summary>Runs for the lifetime of one connection. Returns when the client disconnects.</summary>
    public async Task HandleAsync(WebSocket socket, CancellationToken ct)
    {
        var client = new Client(socket);
        _clients[client.Id] = client;
        _logger.LogInformation("WebSocket client {Id} connected ({Count} connected)", client.Id, _clients.Count);

        var sendLoop = SendLoopAsync(client, ct);
        try
        {
            client.Enqueue(Serialize(new { type = "snapshot", vehicles = _store.GetAll() }));
            await ReceiveLoopAsync(socket, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // Client vanished without a close handshake, or the server is shutting down.
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            client.Outbox.Writer.TryComplete();
            await sendLoop;

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    // Already gone.
                }
            }

            _logger.LogInformation("WebSocket client {Id} disconnected ({Count} connected)", client.Id, _clients.Count);
        }
    }

    // Called on the MQTT thread for every stored position, so it must stay fast.
    private void OnPositionUpdated(VehiclePosition position)
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var message = Serialize(new { type = "position", vehicle = position });
        foreach (var client in _clients.Values)
        {
            client.Enqueue(message);
        }
    }

    // We ignore what clients send, but we must keep reading so close frames and pings are processed.
    private static async Task ReceiveLoopAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[1024];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }
        }
    }

    private static async Task SendLoopAsync(Client client, CancellationToken ct)
    {
        try
        {
            await foreach (var message in client.Outbox.Reader.ReadAllAsync(ct))
            {
                if (client.Socket.State != WebSocketState.Open)
                {
                    break;
                }

                var bytes = Encoding.UTF8.GetBytes(message);
                await client.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // Connection is gone; HandleAsync cleans up.
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private sealed class Client
    {
        public Client(WebSocket socket) => Socket = socket;

        public Guid Id { get; } = Guid.NewGuid();
        public WebSocket Socket { get; }

        public Channel<string> Outbox { get; } = Channel.CreateBounded<string>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });

        public void Enqueue(string message) => Outbox.Writer.TryWrite(message);
    }
}