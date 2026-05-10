import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
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
    <h1 class="mb-6 font-heading text-3xl font-semibold tracking-tight text-foreground">Watchtower Events</h1>

    <div class="overflow-hidden rounded-lg border border-border">
      <table class="w-full text-left text-sm">
        <thead>
          <tr class="border-b border-border bg-secondary/50">
            <th class="px-4 py-3 font-semibold text-foreground">Timestamp</th>
            <th class="px-4 py-3 font-semibold text-foreground">Container</th>
            <th class="px-4 py-3 font-semibold text-foreground">Image</th>
            <th class="px-4 py-3 font-semibold text-foreground">Old ID</th>
            <th class="px-4 py-3 font-semibold text-foreground">New ID</th>
            <th class="px-4 py-3 font-semibold text-foreground">Action</th>
          </tr>
        </thead>
        <tbody>
          <tr *ngFor="let evt of events" class="border-b border-border last:border-b-0"
              [ngClass]="{ 'border-l-4 border-l-green-500': evt.newImageId }">
            <td class="px-4 py-3 text-muted-foreground">{{ evt.timestamp | date:'medium' }}</td>
            <td class="px-4 py-3 text-foreground">{{ evt.containerName }}</td>
            <td class="px-4 py-3 text-foreground">{{ evt.imageName }}</td>
            <td class="mono px-4 py-3 font-mono text-sm text-muted-foreground">{{ evt.oldImageId }}</td>
            <td class="mono px-4 py-3 font-mono text-sm text-muted-foreground">{{ evt.newImageId }}</td>
            <td class="px-4 py-3 text-foreground">{{ evt.action }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <p *ngIf="events.length === 0" class="empty mt-4 italic text-muted-foreground">No watchtower events recorded yet.</p>
  `,
  styles: []
})
export class WatchtowerComponent implements OnInit, OnDestroy {
  events: WatchtowerEvent[] = [];
  private sub?: Subscription;

  constructor(
    private api: ApiService,
    private signalr: SignalrService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.api.getWatchtowerEvents().subscribe({
      next: events => {
        this.events = events;
        this.cdr.detectChanges();
      },
      error: err => console.error('Watchtower API error:', err)
    });

    this.signalr.connect();
    this.sub = this.signalr.onWatchtowerEvent$.subscribe(evt => {
      this.events.unshift(evt);
      this.cdr.detectChanges();
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.signalr.disconnect();
  }
}
