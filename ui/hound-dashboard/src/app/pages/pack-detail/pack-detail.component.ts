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
      <h1 class="mb-2 font-heading text-3xl font-semibold tracking-tight text-foreground">{{ pack.name }}</h1>
      <span class="status inline-block rounded-md px-2 py-0.5 text-sm font-medium"
            [ngClass]="{
              'bg-green-900/40 text-green-400': pack.status.toLowerCase() === 'running',
              'bg-secondary text-secondary-foreground': pack.status.toLowerCase() === 'idle' || pack.status.toLowerCase() === 'stopped',
              'bg-red-900/40 text-red-400': pack.status.toLowerCase() === 'error'
            }">{{ pack.status }}</span>

      <h2 class="mb-3 mt-6 text-xl font-semibold text-foreground">Hounds</h2>
      <div class="grid grid-cols-[repeat(auto-fill,minmax(200px,1fr))] gap-4 mb-8">
        <div *ngFor="let hound of hounds" class="hound-card rounded-lg border border-border bg-card p-4">
          <h3 class="mb-2 text-base font-semibold text-card-foreground">{{ hound.name }}</h3>
          <span class="status inline-block rounded-md px-2 py-0.5 text-sm font-medium"
                [ngClass]="{
                  'bg-green-900/40 text-green-400': hound.status.toLowerCase() === 'processing',
                  'bg-secondary text-secondary-foreground': hound.status.toLowerCase() === 'idle' || hound.status.toLowerCase() === 'disabled',
                  'bg-red-900/40 text-red-400': hound.status.toLowerCase() === 'error'
                }">{{ hound.status }}</span>
        </div>
      </div>

      <h2 class="mb-3 text-xl font-semibold text-foreground">Activity Feed</h2>
      <div class="max-h-96 overflow-y-auto rounded-lg border border-border">
        <div *ngFor="let item of activities"
             class="border-b border-border px-4 py-3"
             [ngClass]="{
               'border-l-4 border-l-red-500': item.severity.toLowerCase() === 'error',
               'border-l-4 border-l-yellow-500': item.severity.toLowerCase() === 'warning',
               'border-l-4 border-l-green-500': item.severity.toLowerCase() === 'success'
             }">
          <span class="mr-2 text-xs text-muted-foreground">{{ item.timestamp | date:'medium' }}</span>
          <strong class="text-foreground">{{ item.houndName }}</strong>: <span class="text-muted-foreground">{{ item.message }}</span>
        </div>
        <p *ngIf="activities.length === 0" class="empty p-4 italic text-muted-foreground">No activity yet.</p>
      </div>
    </div>
  `,
  styles: []
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
