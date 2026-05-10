import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { TradeDocument, OrderUpdate } from '../../models';

@Component({
  selector: 'app-execution',
  standalone: true,
  imports: [CommonModule],
  template: `
    <h1 class="mb-6 text-2xl font-bold text-foreground">Execution Dashboard</h1>

    <section class="mb-8">
      <h2 class="mb-3 text-lg font-semibold text-foreground">Live Order Status</h2>
      <div class="overflow-x-auto rounded-lg border border-border">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-border bg-muted/50">
              <th class="px-4 py-2 text-left font-medium text-muted-foreground">Symbol</th>
              <th class="px-4 py-2 text-left font-medium text-muted-foreground">Action</th>
              <th class="px-4 py-2 text-right font-medium text-muted-foreground">Requested</th>
              <th class="px-4 py-2 text-left font-medium text-muted-foreground">Fill Status</th>
              <th class="px-4 py-2 text-right font-medium text-muted-foreground">Filled Qty</th>
              <th class="px-4 py-2 text-right font-medium text-muted-foreground">Avg Price</th>
              <th class="px-4 py-2 text-left font-medium text-muted-foreground">Execution Time</th>
            </tr>
          </thead>
          <tbody>
            @for (trade of trades; track trade.id) {
              <tr class="border-b border-border last:border-0">
                <td class="px-4 py-2 font-medium text-foreground">{{ trade.symbol }}</td>
                <td class="px-4 py-2 text-foreground">{{ trade.action }}</td>
                <td class="px-4 py-2 text-right text-foreground">{{ trade.requestedQuantity }}</td>
                <td class="px-4 py-2">
                  <span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium"
                        [ngClass]="statusClass(trade.fillStatus)">
                    {{ trade.fillStatus }}
                  </span>
                </td>
                <td class="px-4 py-2 text-right text-foreground">{{ trade.filledQuantity }}</td>
                <td class="px-4 py-2 text-right text-foreground">
                  {{ trade.averageFillPrice ? ('$' + trade.averageFillPrice.toFixed(2)) : '—' }}
                </td>
                <td class="px-4 py-2 text-muted-foreground">
                  {{ trade.executionTime ? (trade.executionTime | date:'short') : '—' }}
                </td>
              </tr>
            }
            @if (trades.length === 0) {
              <tr>
                <td colspan="7" class="px-4 py-8 text-center text-muted-foreground">No trades found</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>

    <div class="flex items-center gap-2">
      <button (click)="prevPage()" [disabled]="page <= 1"
              class="rounded border border-border bg-card px-3 py-1 text-sm text-foreground disabled:opacity-50">
        Previous
      </button>
      <span class="text-sm text-muted-foreground">Page {{ page }}</span>
      <button (click)="nextPage()" [disabled]="trades.length < pageSize"
              class="rounded border border-border bg-card px-3 py-1 text-sm text-foreground disabled:opacity-50">
        Next
      </button>
    </div>
  `,
  styles: []
})
export class ExecutionComponent implements OnInit, OnDestroy {
  trades: TradeDocument[] = [];
  page = 1;
  pageSize = 20;

  private subscription?: Subscription;

  constructor(
    private api: ApiService,
    private signalr: SignalrService,
  ) {}

  ngOnInit(): void {
    this.loadTrades();
    this.signalr.connect();
    this.signalr.subscribeToPack('trading-pack');
    this.subscription = this.signalr.onOrderUpdate$.subscribe((update: OrderUpdate) => {
      this.applyOrderUpdate(update);
    });
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
    this.signalr.unsubscribeFromPack('trading-pack');
  }

  loadTrades(): void {
    this.api.getTrades(this.page, this.pageSize).subscribe(trades => {
      this.trades = trades;
    });
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
}
