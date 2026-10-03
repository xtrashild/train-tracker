import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  ViewEncapsulation,
  afterNextRender,
  effect,
  input,
  model,
  signal,
  viewChild,
} from '@angular/core';
import * as L from 'leaflet';
import { TILES } from './config';
import { lineColor } from './lines';
import { Vehicle } from './vehicle.model';

/** Everything needed to move one marker smoothly from its old to its new position. */
interface Track {
  marker: L.Marker;
  from: L.LatLng;
  to: L.LatLng;
  start: number;
  duration: number;
  lastUpdate: number;
  look: string; // changes when the marker must be redrawn (line, selected, stale)
}

@Component({
  selector: 'app-train-map',
  template: `<div #host class="map" role="application" aria-label="Map of live train positions"></div>`,
  styleUrl: './train-map.css',
  encapsulation: ViewEncapsulation.None, // Leaflet creates its own DOM, so styles must be global
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrainMap implements OnDestroy {
  readonly vehicles = input.required<Vehicle[]>();
  readonly selectedId = model<string | null>(null);

  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private readonly ready = signal(false);

  private map?: L.Map;
  private readonly tracks = new Map<string, Track>();
  private frame = 0;
  private fitted = false;
  private lastSelected: string | null = null;
  private readonly reducedMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;

  constructor() {
    afterNextRender(() => {
      this.initMap();
      this.ready.set(true);
    });

    effect(() => {
      if (!this.ready()) {
        return;
      }
      this.sync(this.vehicles(), this.selectedId());
    });
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.frame);
    this.map?.remove();
  }

  private initMap(): void {
    this.map = L.map(this.host().nativeElement, { center: [51.04, 13.74], zoom: 9, zoomControl: true });

    L.tileLayer(TILES.url, { maxZoom: TILES.maxZoom, attribution: TILES.attribution }).addTo(this.map);

    this.frame = requestAnimationFrame(this.animate);
  }

  private sync(vehicles: Vehicle[], selectedId: string | null): void {
    const map = this.map;
    if (!map) {
      return;
    }

    const now = performance.now();
    const seen = new Set<string>();

    for (const v of vehicles) {
      seen.add(v.vehicleId);
      const target = L.latLng(v.lat, v.lon);
      const look = `${v.lineId}|${v.vehicleId === selectedId}|${v.stale}`;
      const track = this.tracks.get(v.vehicleId);

      if (!track) {
        const marker = L.marker(target, { icon: this.icon(v, selectedId), keyboard: false, title: v.vehicleId });
        marker.on('click', () => this.selectedId.set(v.vehicleId));
        marker.addTo(map);
        this.tracks.set(v.vehicleId, { marker, from: target, to: target, start: now, duration: 0, lastUpdate: now, look });
        continue;
      }

      if (!track.to.equals(target)) {
        if (this.reducedMotion) {
          track.marker.setLatLng(target);
          track.from = target;
          track.duration = 0;
        } else {
          track.from = track.marker.getLatLng();
          track.duration = Math.min(Math.max(now - track.lastUpdate, 500), 5000);
        }
        track.to = target;
        track.start = now;
        track.lastUpdate = now;
      }

      if (track.look !== look) {
        track.look = look;
        track.marker.setIcon(this.icon(v, selectedId));
        track.marker.setZIndexOffset(v.vehicleId === selectedId ? 1000 : 0);
      }
    }

    for (const [id, track] of this.tracks) {
      if (!seen.has(id)) {
        track.marker.remove();
        this.tracks.delete(id);
      }
    }

    if (!this.fitted && vehicles.length > 0) {
      this.fitted = true;
      map.fitBounds(L.latLngBounds(vehicles.map((v) => L.latLng(v.lat, v.lon))), { padding: [48, 48] });
    }

    if (selectedId !== this.lastSelected) {
      this.lastSelected = selectedId;
      const selected = selectedId ? this.tracks.get(selectedId) : undefined;
      if (selected) {
        map.panTo(selected.marker.getLatLng());
      }
    }
  }

  /** Runs every frame: moves each marker part of the way towards its latest position. */
  private readonly animate = (time: number): void => {
    for (const track of this.tracks.values()) {
      if (track.duration <= 0 || track.marker.getLatLng().equals(track.to)) {
        continue;
      }
      const f = Math.min(1, (time - track.start) / track.duration);
      track.marker.setLatLng(
        L.latLng(
          track.from.lat + (track.to.lat - track.from.lat) * f,
          track.from.lng + (track.to.lng - track.from.lng) * f,
        ),
      );
    }
    this.frame = requestAnimationFrame(this.animate);
  };

  private icon(v: Vehicle, selectedId: string | null): L.DivIcon {
    const pin = document.createElement('span');
    pin.className = 'pin';
    pin.classList.toggle('pin-selected', v.vehicleId === selectedId);
    pin.classList.toggle('pin-stale', v.stale);
    pin.style.setProperty('--c', lineColor(v.lineId));
    pin.textContent = v.lineId;

    // Zero-size icon box: the pin centres itself on the coordinate with CSS.
    return L.divIcon({ html: pin, className: 'pin-anchor', iconSize: [0, 0], iconAnchor: [0, 0] });
  }
}