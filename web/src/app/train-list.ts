import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  model,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ConnectionStatus } from './live-vehicles.service';
import { lineColor } from './lines';
import { Vehicle } from './vehicle.model';

interface LineGroup {
  lineId: string;
  vehicles: Vehicle[];
}

@Component({
  selector: 'app-train-list',
  imports: [DecimalPipe],
  templateUrl: './train-list.html',
  styleUrl: './train-list.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrainList {
  readonly vehicles = input.required<Vehicle[]>();
  readonly status = input.required<ConnectionStatus>();
  readonly selectedId = model<string | null>(null);

  protected readonly groups = computed<LineGroup[]>(() => {
    const byLine = new Map<string, Vehicle[]>();
    for (const v of this.vehicles()) {
      byLine.set(v.lineId, [...(byLine.get(v.lineId) ?? []), v]);
    }
    return [...byLine].map(([lineId, vehicles]) => ({ lineId, vehicles }));
  });

  protected readonly statusLabel = computed(() => {
    switch (this.status()) {
      case 'live':
        return 'Live';
      case 'connecting':
        return 'Connecting';
      case 'reconnecting':
        return 'Connection lost. Retrying';
    }
  });

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    // When a train is picked on the map, bring its row into view.
    effect(() => {
      const id = this.selectedId();
      if (!id) {
        return;
      }
      queueMicrotask(() => {
        this.element.nativeElement
          .querySelector(`[data-id="${CSS.escape(id)}"]`)
          ?.scrollIntoView({ block: 'nearest' });
      });
    });
  }

  protected color(lineId: string): string {
    return lineColor(lineId);
  }

  protected toggle(id: string): void {
    this.selectedId.set(this.selectedId() === id ? null : id);
  }

  protected delayText(v: Vehicle): string {
    if (v.stale) {
      return 'No signal';
    }
    return v.delaySeconds < 60 ? 'On time' : `+${Math.round(v.delaySeconds / 60)} min`;
  }

  protected delayClass(v: Vehicle): string {
    if (v.stale) {
      return 'delay-none';
    }
    if (v.delaySeconds < 60) {
      return 'delay-ok';
    }
    return v.delaySeconds < 300 ? 'delay-minor' : 'delay-major';
  }
}