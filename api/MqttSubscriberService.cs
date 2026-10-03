using System.Text.Json;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;

namespace Api;

/// <summary>Background service: stays connected to the broker, subscribes to the vehicle topic
/// and writes every valid position message into the <see cref="VehicleStore"/>.</summary>
public sealed class MqttSubscriberService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MqttOptions _options;
    private readonly VehicleStore _store;
    private readonly ILogger<MqttSubscriberService> _logger;

    public MqttSubscriberService(
        IOptions<MqttOptions> options,
        VehicleStore store,
        ILogger<MqttSubscriberService> logger)
    {
        _options = options.Value;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += HandleMessageAsync;

        var connectOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId($"api-{Guid.NewGuid():N}")
            .Build();

        var subscribeOptions = factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(_options.Topic))
            .Build();

        // Check the connection every 2 seconds and reconnect (and resubscribe) if it was lost.
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!client.IsConnected)
            {
                try
                {
                    await client.ConnectAsync(connectOptions, stoppingToken);
                    await client.SubscribeAsync(subscribeOptions, stoppingToken);
                    _logger.LogInformation(
                        "Connected to {Host}:{Port}, subscribed to {Topic}",
                        _options.Host, _options.Port, _options.Topic);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("MQTT connection failed ({Message}). Retrying in 2 s.", ex.Message);
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (client.IsConnected)
        {
            await client.DisconnectAsync();
        }
    }

    private Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        try
        {
            var payload = e.ApplicationMessage.ConvertPayloadToString();
            var position = JsonSerializer.Deserialize<VehiclePosition>(payload, JsonOptions);

            if (position is null || string.IsNullOrWhiteSpace(position.VehicleId))
            {
                _logger.LogWarning("Ignoring message without a vehicle id on {Topic}", topic);
                return Task.CompletedTask;
            }

            _store.Upsert(position);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Ignoring malformed message on {Topic}: {Message}", topic, ex.Message);
        }

        return Task.CompletedTask;
    }
}