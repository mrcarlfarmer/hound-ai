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
  templateUrl: './watchtower.component.html',
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
