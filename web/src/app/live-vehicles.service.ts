import { Injectable, OnDestroy, computed, signal } from '@angular/core';
import { WS_URL } from './config';
import { ServerMessage, Vehicle, VehiclePosition } from './vehicle.model';

export type ConnectionStatus = 'connecting' | 'live' | 'reconnecting';

/** A vehicle is shown as "no signal" after this long without an update. */
const STALE_AFTER_MS = 10_000;
const MAX_RETRY_DELAY_MS = 10_000;

interface Entry {
  position: VehiclePosition;
  receivedAt: number;
}

/** Keeps one WebSocket open to the API and exposes the latest vehicles as signals. */
@Injectable({ providedIn: 'root' })
export class LiveVehiclesService implements OnDestroy {
  readonly status = signal<ConnectionStatus>('connecting');

  private readonly entries = signal<ReadonlyMap<string, Entry>>(new Map());
  private readonly now = signal(Date.now());

  /** Sorted by line, then id. Recomputed every second so the stale flag stays current. */
  readonly vehicles = computed<Vehicle[]>(() => {
    const now = this.now();
    return [...this.entries().values()]
      .map(({ position, receivedAt }) => ({ ...position, stale: now - receivedAt > STALE_AFTER_MS }))
      .sort((a, b) => a.lineId.localeCompare(b.lineId) || a.vehicleId.localeCompare(b.vehicleId));
  });

  private socket?: WebSocket;
  private retryTimer?: ReturnType<typeof setTimeout>;
  private retryCount = 0;
  private destroyed = false;
  private readonly clock = setInterval(() => this.now.set(Date.now()), 1000);

  constructor() {
    this.connect();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    clearInterval(this.clock);
    clearTimeout(this.retryTimer);
    this.socket?.close();
  }

  private connect(): void {
    this.status.set(this.retryCount === 0 ? 'connecting' : 'reconnecting');

    const socket = new WebSocket(WS_URL);
    this.socket = socket;

    socket.onopen = () => {
      this.retryCount = 0;
      this.status.set('live');
    };

    socket.onmessage = (event: MessageEvent<string>) => this.handle(event.data);

    // A failed attempt also ends in "close", so retrying only here avoids scheduling twice.
    socket.onclose = () => {
      if (this.destroyed) {
        return;
      }
      this.status.set('reconnecting');
      const delay = Math.min(1000 * 2 ** this.retryCount, MAX_RETRY_DELAY_MS);
      this.retryCount++;
      this.retryTimer = setTimeout(() => this.connect(), delay);
    };
  }

  private handle(raw: string): void {
    let message: ServerMessage;
    try {
      message = JSON.parse(raw) as ServerMessage;
    } catch {
      return; // not JSON, ignore
    }

    const receivedAt = Date.now();

    if (message.type === 'snapshot') {
      // A snapshot is the full truth, so it replaces everything (also after a reconnect).
      this.entries.set(new Map(message.vehicles.map((p) => [p.vehicleId, { position: p, receivedAt }])));
    } else if (message.type === 'position') {
      const current = this.entries().get(message.vehicle.vehicleId);
      if (current && Date.parse(current.position.timestamp) > Date.parse(message.vehicle.timestamp)) {
        return; // older than what we already have
      }
      const next = new Map(this.entries());
      next.set(message.vehicle.vehicleId, { position: message.vehicle, receivedAt });
      this.entries.set(next);
    }
  }
}