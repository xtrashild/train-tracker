# Train tracker

A live map of German regional trains. Work in progress.

Timetable and delay data (GTFS and GTFS-Realtime) do not include GPS positions, so train
positions are estimated from the schedule plus current delays. Until the real data pipeline
is in place, a simulator publishes fake positions.

## Architecture

```
simulator --MQTT--> Mosquitto broker --MQTT--> API (ASP.NET Core) --> (planned) SignalR --> Angular map
```

## Run locally

Requirements: Docker, .NET SDK.

```bash
docker compose up -d                 # MQTT broker on port 1883
cd simulator && dotnet run           # publishes 10 simulated trains
cd api && dotnet run                 # in a second terminal
curl http://localhost:5080/api/vehicles
```

Watch the raw messages: `mosquitto_sub -h 127.0.0.1 -t 'vehicles/#' -v`

## Status

- [x] MQTT broker and simulator
- [x] API subscribes and exposes the latest positions
- [ ] Persistence in SQL (EF Core)
- [ ] Live push to the browser (SignalR)
- [ ] Angular and Leaflet map
- [ ] Real GTFS / GTFS-RT ingestion with estimated positions