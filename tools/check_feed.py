import requests
from google.transit import gtfs_realtime_pb2

url = "https://realtime.gtfs.de/realtime-free.pb"
feed = gtfs_realtime_pb2.FeedMessage()
feed.ParseFromString(requests.get(url, timeout=30).content)

counts = {"trip_update": 0, "vehicle": 0, "alert": 0}
for e in feed.entity:
    for k in counts:
        if e.HasField(k):
            counts[k] += 1

print(len(feed.entity), "entities:", counts)

for e in feed.entity:
    if e.HasField("trip_update"):
        tu = e.trip_update
        print("trip_id:", tu.trip.trip_id)
        print("route_id:", tu.trip.route_id, "start:", tu.trip.start_date, tu.trip.start_time)
        for stu in tu.stop_time_update[:3]:
            print(" stop_id:", stu.stop_id, "seq:", stu.stop_sequence,
                  "arr_delay:", stu.arrival.delay, "dep_delay:", stu.departure.delay)
        break

print("feed timestamp:", feed.header.timestamp)
import csv, requests
from collections import Counter
from google.transit import gtfs_realtime_pb2

feed = gtfs_realtime_pb2.FeedMessage()
feed.ParseFromString(requests.get("https://realtime.gtfs.de/realtime-free.pb", timeout=30).content)

with open("gtfs/trips.txt", newline="", encoding="utf-8-sig") as f:
    static_trips = {r["trip_id"] for r in csv.DictReader(f)}
with open("gtfs/stops.txt", newline="", encoding="utf-8-sig") as f:
    static_stops = {r["stop_id"] for r in csv.DictReader(f)}

trips = stops = trip_hits = stop_hits = 0
delays = []
for e in feed.entity:
    if not e.HasField("trip_update"):
        continue
    tu = e.trip_update
    trips += 1
    trip_hits += tu.trip.trip_id in static_trips
    for s in tu.stop_time_update:
        stops += 1
        stop_hits += s.stop_id in static_stops
        if s.HasField("arrival"):
            delays.append(s.arrival.delay)

print(f"trip_id match: {trip_hits}/{trips} ({100*trip_hits/trips:.1f}%)")
print(f"stop_id match: {stop_hits}/{stops} ({100*stop_hits/stops:.1f}%)")
nonzero = [d for d in delays if d]
print(f"non-zero arrival delays: {len(nonzero)}/{len(delays)}, max {max(delays, default=0)} s")

import statistics as st
nz = sorted(d for d in delays if d)
print("median:", st.median(nz), "p90:", nz[int(len(nz)*0.9)], "p99:", nz[int(len(nz)*0.99)])
print("negative (early):", sum(d < 0 for d in nz), "over 1h:", sum(d > 3600 for d in nz))