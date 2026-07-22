import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin, Subscription } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { TradeDocument, OrderUpdate, PositionInfo, AlpacaSyncResult } from '../../models';

@Component({
  selector: 'app-execution',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './execution.component.html',
  styles: []
})
export class ExecutionComponent implements OnInit, OnDestroy {
  trades: TradeDocument[] = [];
  page = 1;
  pageSize = 20;
  closingSymbol: string | null = null;
  closeError: string | null = null;
  closeInfo: string | null = null;
  openSymbols = new Set<string>();

  syncing = false;
  syncError: string | null = null;
  lastSync: AlpacaSyncResult | null = null;
  /** Seconds between automatic UI-triggered syncs. Matches the server background interval. */
  private readonly autoSyncIntervalMs = 60_000;
  private autoSyncHandle?: ReturnType<typeof setInterval>;

  private subscription?: Subscription;

  constructor(
    private api: ApiService,
    private signalr: SignalrService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.loadTrades();
    this.signalr.connect();
    this.signalr.subscribeToPack('trading-pack');
    this.subscription = this.signalr.onOrderUpdate$.subscribe((update: OrderUpdate) => {
      this.applyOrderUpdate(update);
    });

    // Kick off a sync immediately so the table reflects Alpaca on load,
    // then poll periodically as a safety net for missed SignalR events.
    this.syncFromAlpaca(true);
    this.autoSyncHandle = setInterval(() => this.syncFromAlpaca(true), this.autoSyncIntervalMs);
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
    this.signalr.unsubscribeFromPack('trading-pack');
    if (this.autoSyncHandle) {
      clearInterval(this.autoSyncHandle);
      this.autoSyncHandle = undefined;
    }
  }

  loadTrades(): void {
    forkJoin({
      trades: this.api.getTrades(this.page, this.pageSize),
      positions: this.api.getPositions(),
    }).subscribe({
      next: ({ trades, positions }) => {
        this.trades = trades;
        this.openSymbols = new Set(positions.map((p: PositionInfo) => p.symbol.toUpperCase()));
        this.cdr.detectChanges();
      },
      error: () => {
        // If positions endpoint fails, still show trades but with no Close buttons.
        this.api.getTrades(this.page, this.pageSize).subscribe(trades => {
          this.trades = trades;
          this.openSymbols = new Set();
          this.cdr.detectChanges();
        });
      },
    });
  }

  hasOpenPosition(symbol: string): boolean {
    return this.openSymbols.has(symbol.toUpperCase());
  }

  /**
   * Returns true if there is an in-flight Sell order for the symbol (Pending or
   * PartiallyFilled). Used to suppress the Close button so we don't queue a
   * duplicate liquidation while one is already working.
   */
  hasPendingSell(symbol: string): boolean {
    const sym = symbol.toUpperCase();
    return this.trades.some(t =>
      t.symbol.toUpperCase() === sym &&
      t.action?.toUpperCase() === 'SELL' &&
      (t.fillStatus === 'Pending' || t.fillStatus === 'PartiallyFilled'));
  }

  canClose(trade: TradeDocument): boolean {
    return trade.fillStatus === 'Filled'
      && trade.action?.toUpperCase() === 'BUY'
      && this.hasOpenPosition(trade.symbol)
      && !this.hasPendingSell(trade.symbol);
  }

  prevPage(): void {
    if (this.page > 1) {
      this.page--;
      this.loadTrades();
    }
  }

  nextPage(): void {
    this.page++;
    this.loadTrades();
  }

  statusClass(status: string): Record<string, boolean> {
    return {
      'bg-green-100 text-green-800': status === 'Filled',
      'bg-yellow-100 text-yellow-800': status === 'PartiallyFilled',
      'bg-blue-100 text-blue-800': status === 'Pending',
      'bg-red-100 text-red-800': status === 'Canceled' || status === 'Rejected' || status === 'Expired',
    };
  }

  private applyOrderUpdate(update: OrderUpdate): void {
    const trade = this.trades.find(t => t.id === update.tradeDocumentId);
    if (trade) {
      trade.fillStatus = update.fillStatus as TradeDocument['fillStatus'];
      trade.filledQuantity = update.filledQuantity;
      trade.averageFillPrice = update.averageFillPrice;
      trade.executionTime = update.executionTime;
    } else {
      this.loadTrades();
    }
  }

  closePosition(symbol: string): void {
    if (this.closingSymbol) return;
    if (!confirm(`Liquidate the entire ${symbol} position?`)) return;
    this.closingSymbol = symbol;
    this.closeError = null;
    this.closeInfo = null;
    this.api.closePosition(symbol).subscribe({
      next: () => {
        this.closingSymbol = null;
        this.loadTrades();
      },
      error: (err) => {
        this.closingSymbol = null;
        const status = (err as { status?: number })?.status;
        // 409 = position already closed / nothing to sell. Treat as informational
        // and drop the symbol from openSymbols so the Close button disappears.
        if (status === 409) {
          this.openSymbols.delete(symbol.toUpperCase());
          this.closeInfo = this.formatProblem(symbol, err);
        } else {
          this.closeError = this.formatProblem(symbol, err);
        }
        this.cdr.detectChanges();
      },
    });
  }

  private formatProblem(symbol: string, err: unknown): string {
    const problem = (err as { error?: { title?: string; detail?: string } })?.error;
    const status = (err as { status?: number })?.status;
    const title = problem?.title;
    const detail = problem?.detail;
    if (title && detail) return `${symbol}: ${title} — ${detail}`;
    if (detail) return `${symbol}: ${detail}`;
    if (title) return `${symbol}: ${title}`;
    const message = (err as { message?: string })?.message;
    if (status === 0) return `${symbol}: Could not reach the API.`;
    return `${symbol}: ${message ?? 'unknown error'}`;
  }

  /**
   * Manually or automatically reconcile against Alpaca.
   * SignalR will push individual OnOrderUpdate events for each change, but we also
   * refresh the table once the sync completes to pick up new positions/trades.
   */
  syncFromAlpaca(silent = false): void {
    if (this.syncing) return;
    this.syncing = true;
    if (!silent) this.syncError = null;
    this.api.syncTradesFromAlpaca().subscribe({
      next: (result) => {
        this.syncing = false;
        this.lastSync = result;
        // Always refresh local view to also reconcile openSymbols / new rows.
        this.loadTrades();
      },
      error: (err) => {
        this.syncing = false;
        if (!silent) {
          this.syncError = this.formatProblem('Sync', err);
        }
        this.cdr.detectChanges();
      },
    });
  }
}
