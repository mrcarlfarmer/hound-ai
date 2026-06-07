import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { Pack, HoundInfo, ActivityLog, DebateTurnMetadata, isDebateTurn } from '../../models';
import { Subscription } from 'rxjs';

interface DebateTurnView {
  id: string;
  role: 'Bull' | 'Bear';
  turnIndex: number;
  symbol: string;
  message: string;
  timestamp: string;
}

@Component({
  selector: 'app-pack-detail',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pack-detail.component.html',
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
    private signalr: SignalrService,
    private cdr: ChangeDetectorRef,
  ) {}

  /**
   * Debate turns extracted from the activity feed, ordered oldest → newest
   * within each symbol. Used by the "Strategy Debate" panel to render the
   * bull-vs-bear conversation that preceded the most recent decision.
   */
  get debateTurns(): DebateTurnView[] {
    const turns: DebateTurnView[] = [];
    for (const log of this.activities) {
      if (!isDebateTurn(log)) continue;
      const meta = log.metadata as DebateTurnMetadata;
      turns.push({
        id: log.id,
        role: meta.role,
        turnIndex: meta.turnIndex,
        symbol: meta.symbol,
        message: meta.fullMessage,
        timestamp: log.timestamp,
      });
    }
    // Newest symbol's debate first; within a symbol, oldest turn first.
    turns.sort((a, b) => {
      if (a.symbol !== b.symbol) {
        return b.timestamp.localeCompare(a.timestamp);
      }
      return a.turnIndex - b.turnIndex;
    });
    return turns;
  }

  ngOnInit(): void {
    const packId = this.route.snapshot.paramMap.get('id')!;
    this.api.getPack(packId).subscribe(p => { this.pack = p; this.cdr.detectChanges(); });
    this.api.getHounds(packId).subscribe(h => { this.hounds = h; this.cdr.detectChanges(); });

    this.signalr.connect();
    this.signalr.subscribeToPack(packId);
    this.sub = this.signalr.onActivity$.subscribe(a => {
      this.activities.unshift(a);
      this.cdr.detectChanges();
    });
  }

  ngOnDestroy(): void {
    if (this.pack) this.signalr.unsubscribeFromPack(this.pack.id);
    this.sub?.unsubscribe();
    this.signalr.disconnect();
  }
}
