using Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
builder.Services.AddSingleton<VehicleStore>();
builder.Services.AddHostedService<MqttSubscriberService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/vehicles", (VehicleStore store) => store.GetAll());

app.MapGet("/api/vehicles/{id}", (string id, VehicleStore store) =>
    store.TryGet(id, out var vehicle) ? Results.Ok(vehicle) : Results.NotFound());

app.Run();