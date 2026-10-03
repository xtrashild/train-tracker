using Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
builder.Services.AddSingleton<VehicleStore>();
builder.Services.AddSingleton<WebSocketHub>();
builder.Services.AddHostedService<MqttSubscriberService>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/vehicles", (VehicleStore store) => store.GetAll());

app.MapGet("/api/vehicles/{id}", (string id, VehicleStore store) =>
    store.TryGet(id, out var vehicle) ? Results.Ok(vehicle) : Results.NotFound());

// Live updates: browsers connect with new WebSocket("ws://localhost:5080/ws")
app.MapGet("/ws", async (HttpContext context, WebSocketHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.HandleAsync(socket, context.RequestAborted);
});

app.Run();