import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { WatchtowerEvent } from '../../models';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-watchtower',
  standalone: true,
  imports: [CommonModule],
  template: `
    <h1>Watchtower Events</h1>

    <table class="events-table" *ngIf="events.length > 0">
      <thead>
        <tr>
          <th>Timestamp</th>
          <th>Container</th>
          <th>Image</th>
          <th>Old ID</th>
          <th>New ID</th>
          <th>Action</th>
        </tr>
      </thead>
      <tbody>
        <tr *ngFor="let evt of events" [class.updated]="evt.newImageId">
          <td>{{ evt.timestamp | date:'medium' }}</td>
          <td>{{ evt.containerName }}</td>
          <td>{{ evt.imageName }}</td>
          <td class="mono">{{ evt.oldImageId }}</td>
          <td class="mono">{{ evt.newImageId }}</td>
          <td>{{ evt.action }}</td>
        </tr>
      </tbody>
    </table>

    <p *ngIf="events.length === 0" class="empty">No watchtower events recorded yet.</p>
  `,
  styles: [`
    .events-table { width: 100%; border-collapse: collapse; font-size: 0.9rem; }
    .events-table th, .events-table td { padding: 0.5rem 0.75rem; border-bottom: 1px solid #eee; text-align: left; }
    .events-table th { background: #f5f5f5; font-weight: 600; }
    .events-table tr.updated { border-left: 3px solid #28a745; }
    .mono { font-family: monospace; font-size: 0.85rem; }
    .empty { color: #666; font-style: italic; }
  `]
})
export class WatchtowerComponent implements OnInit, OnDestroy {
  events: WatchtowerEvent[] = [];
  private sub?: Subscription;

  constructor(
    private api: ApiService,
    private signalr: SignalrService
  ) {}

  ngOnInit(): void {
    this.api.getWatchtowerEvents().subscribe(events => this.events = events);

    this.signalr.connect();
    this.sub = this.signalr.onWatchtowerEvent$.subscribe(evt => {
      this.events.unshift(evt);
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.signalr.disconnect();
  }
}
