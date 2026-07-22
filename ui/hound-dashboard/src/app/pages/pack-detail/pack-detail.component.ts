import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import {
  Pack, HoundInfo, ActivityLog,
  DebateTurnMetadata, isDebateTurn,
  StrategyDecisionMetadata, isStrategyDecision,
} from '../../models';
import { Subscription } from 'rxjs';

interface DebateTurnView {
  id: string;
  role: 'Bull' | 'Bear';
  turnIndex: number;
  symbol: string;
  message: string;
  timestamp: string;
}

/**
 * Coordinator verdict that closes a debate. Surfaced beneath the transcript
 * so the outcome of the bull-vs-bear conversation is immediately obvious.
 */
interface VerdictView {
  symbol: string;
  action: 'Buy' | 'Sell' | 'Hold';
  quantity: number;
  confidence: number;
  timestamp: string;
}

/**
 * A debate transcript for a single symbol, paired with its coordinator
 * verdict (if one has been emitted yet). Drives the grouped layout in the
 * pack-detail "Strategy Debate" panel.
 */
interface DebateGroupView {
  symbol: string;
  turns: DebateTurnView[];
  verdict: VerdictView | null;
  latestTurnTimestamp: string;
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

  /**
   * Debate turns grouped by symbol and paired with the coordinator's verdict
   * (if it has been emitted yet). Newest-symbol-first; within each group
   * turns are in ascending order so the conversation reads top-to-bottom and
   * the verdict banner sits beneath the closing argument.
   */
  get debateGroups(): DebateGroupView[] {
    const byId = new Map<string, DebateGroupView>();
    for (const turn of this.debateTurns) {
      let group = byId.get(turn.symbol);
      if (!group) {
        group = { symbol: turn.symbol, turns: [], verdict: null, latestTurnTimestamp: turn.timestamp };
        byId.set(turn.symbol, group);
      }
      group.turns.push(turn);
      if (turn.timestamp > group.latestTurnTimestamp) {
        group.latestTurnTimestamp = turn.timestamp;
      }
    }
    const verdicts = this.verdictsBySymbol;
    for (const group of byId.values()) {
      const v = verdicts[group.symbol];
      // Only attach the verdict if it came AFTER the last debate turn —
      // a stale verdict from a previous cycle should not be shown above a
      // freshly-started debate that hasn't concluded yet.
      if (v && v.timestamp >= group.latestTurnTimestamp) {
        group.verdict = v;
      }
    }
    return Array.from(byId.values())
      .sort((a, b) => b.latestTurnTimestamp.localeCompare(a.latestTurnTimestamp));
  }

  /**
   * Maps a symbol → the most recent coordinator verdict observed in the
   * activity feed. Used by the template to render a "Coordinator Verdict"
   * banner beneath each symbol's debate transcript so the outcome of the
   * bull-vs-bear conversation is unmistakable.
   */
  get verdictsBySymbol(): Record<string, VerdictView> {
    const map: Record<string, VerdictView> = {};
    // activities is newest-first (unshift on push); first hit per symbol wins.
    for (const log of this.activities) {
      if (!isStrategyDecision(log)) continue;
      const meta = log.metadata as StrategyDecisionMetadata;
      if (map[meta.symbol]) continue;
      map[meta.symbol] = {
        symbol: meta.symbol,
        action: meta.action,
        quantity: meta.quantity,
        confidence: meta.confidence,
        timestamp: log.timestamp,
      };
    }
    return map;
  }

  /** Background/border styling for the verdict banner, keyed off the action. */
  verdictBannerClass(action?: string): string {
    switch (action?.toLowerCase()) {
      case 'buy': return 'bg-green-900/20 border-green-600 text-green-100';
      case 'sell': return 'bg-red-900/20 border-red-600 text-red-100';
      default: return 'bg-yellow-900/20 border-yellow-600 text-yellow-100';
    }
  }

  /** Pill styling for the action label inside the verdict banner. */
  actionPillClass(action?: string): string {
    switch (action?.toLowerCase()) {
      case 'buy': return 'bg-green-900/40 text-green-400 border-green-600';
      case 'sell': return 'bg-red-900/40 text-red-400 border-red-600';
      default: return 'bg-yellow-900/40 text-yellow-400 border-yellow-600';
    }
  }

  /** Formats a 0–1 confidence score as a percent string for display. */
  confidencePercent(score?: number): string {
    if (score == null || isNaN(score)) return '—';
    return `${Math.round(score * 100)}%`;
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
