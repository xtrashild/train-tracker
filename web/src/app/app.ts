import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { LiveVehiclesService } from './live-vehicles.service';
import { TrainList } from './train-list';
import { TrainMap } from './train-map';

@Component({
  selector: 'app-root',
  imports: [TrainList, TrainMap],
  template: `
    <app-train-list [vehicles]="vehicles()" [status]="status()" [(selectedId)]="selectedId" />
    <app-train-map [vehicles]="vehicles()" [(selectedId)]="selectedId" />
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: 340px 1fr;
      height: 100%;
    }

    @media (max-width: 700px) {
      :host {
        grid-template-columns: 1fr;
        grid-template-rows: 1fr 45%;
      }

      app-train-list {
        order: 2;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly live = inject(LiveVehiclesService);

  protected readonly vehicles = this.live.vehicles;
  protected readonly status = this.live.status;
  protected readonly selectedId = signal<string | null>(null);
}