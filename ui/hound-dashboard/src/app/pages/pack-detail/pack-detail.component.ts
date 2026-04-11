import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { Pack, HoundInfo, ActivityLog } from '../../models';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-pack-detail',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div *ngIf="pack">
      <h1>{{ pack.name }}</h1>
      <span class="status" [class]="pack.status.toLowerCase()">{{ pack.status }}</span>

      <h2>Hounds</h2>
      <div class="hound-grid">
        <div *ngFor="let hound of hounds" class="hound-card">
          <h3>{{ hound.name }}</h3>
          <span class="status" [class]="hound.status.toLowerCase()">{{ hound.status }}</span>
        </div>
      </div>

      <h2>Activity Feed</h2>
      <div class="activity-feed">
        <div *ngFor="let item of activities" class="activity-item" [class]="item.severity.toLowerCase()">
          <span class="time">{{ item.timestamp | date:'medium' }}</span>
          <strong>{{ item.houndName }}</strong>: {{ item.message }}
        </div>
        <p *ngIf="activities.length === 0" class="empty">No activity yet.</p>
      </div>
    </div>
  `,
  styles: [`
    .hound-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 1rem; margin-bottom: 2rem; }
    .hound-card { padding: 1rem; border: 1px solid #ddd; border-radius: 8px; }
    .status { padding: 2px 8px; border-radius: 4px; font-size: 0.85rem; }
    .activity-feed { max-height: 400px; overflow-y: auto; }
    .activity-item { padding: 0.5rem; border-bottom: 1px solid #eee; }
    .activity-item.error { border-left: 3px solid #dc3545; }
    .activity-item.warning { border-left: 3px solid #ffc107; }
    .activity-item.success { border-left: 3px solid #28a745; }
    .time { font-size: 0.8rem; color: #666; margin-right: 0.5rem; }
    .empty { color: #666; font-style: italic; }
  `]
})
export class PackDetailComponent implements OnInit, OnDestroy {
  pack?: Pack;
  hounds: HoundInfo[] = [];
  activities: ActivityLog[] = [];
  private sub?: Subscription;

  constructor(
    private route: ActivatedRoute,
    private api: ApiService,
    private signalr: SignalrService
  ) {}

  ngOnInit(): void {
    const packId = this.route.snapshot.paramMap.get('id')!;
    this.api.getPack(packId).subscribe(p => this.pack = p);
    this.api.getHounds(packId).subscribe(h => this.hounds = h);

    this.signalr.connect();
    this.signalr.subscribeToPack(packId);
    this.sub = this.signalr.onActivity$.subscribe(a => {
      this.activities.unshift(a);
    });
  }

  ngOnDestroy(): void {
    if (this.pack) this.signalr.unsubscribeFromPack(this.pack.id);
    this.sub?.unsubscribe();
    this.signalr.disconnect();
  }
}
