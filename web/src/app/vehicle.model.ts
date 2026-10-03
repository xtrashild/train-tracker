/** One position report, exactly as the API sends it. */
export interface VehiclePosition {
  vehicleId: string;
  lat: number;
  lon: number;
  lineId: string;
  delaySeconds: number;
  estimated: boolean;
  speedKmh: number;
  timestamp: string;
}

/** A position plus what the browser knows about it. */
export interface Vehicle extends VehiclePosition {
  /** True when nothing has been received for this vehicle for a while. */
  stale: boolean;
}

/** Messages the API sends over the WebSocket. */
export type ServerMessage =
  | { type: 'snapshot'; vehicles: VehiclePosition[] }
  | { type: 'position'; vehicle: VehiclePosition };