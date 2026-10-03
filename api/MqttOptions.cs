namespace Api;

/// <summary>Settings for the MQTT connection. Bound from the "Mqtt" section of appsettings.json.
/// Can be overridden with environment variables, e.g. Mqtt__Host=192.168.1.10.</summary>
public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string Topic { get; set; } = "vehicles/#";
}