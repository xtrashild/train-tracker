# Train tracker

A live map of German regional trains. Work in progress.

Timetable and delay data (GTFS and GTFS-Realtime) do not include GPS positions, so train
positions are estimated from the schedule plus current delays. Until the real data pipeline
is in place, a simulator publishes fake positions.

## Architecture

```
simulator --MQTT--> Mosquitto broker --MQTT--> API (ASP.NET Core) --WebSocket--> Angular + Leaflet map
```

## Run locally

Requirements: Docker, .NET SDK (with the ASP.NET Core runtime), Node.js.

```bash
docker compose up -d                 # MQTT broker (1883) and PostgreSQL (5432)
cd simulator && dotnet run           # publishes 10 simulated trains
cd api && dotnet run                 # in a second terminal
cd web && npm install && npm start   # third terminal, then open http://localhost:4200
curl http://localhost:5080/api/vehicles
```

Watch the raw messages: `mosquitto_sub -h 127.0.0.1 -t 'vehicles/#' -v`

## Status

- [x] MQTT broker and simulator
- [x] API subscribes and exposes the latest positions
- [ ] PostgreSQL timetable (EF Core migrations) and GTFS import
- [x] Live push to the browser (raw WebSocket at /ws)
- [x] Angular and Leaflet map
- [ ] Position estimator with unit tests
- [ ] Real-time worker: GTFS-RT delays to estimated positions on MQTT
- [ ] Everything in Docker, deployed demo