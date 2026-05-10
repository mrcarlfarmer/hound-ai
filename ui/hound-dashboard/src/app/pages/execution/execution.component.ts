import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { TradeDocument, OrderUpdate } from '../../models';

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
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
    this.signalr.unsubscribeFromPack('trading-pack');
  }

  loadTrades(): void {
    this.api.getTrades(this.page, this.pageSize).subscribe(trades => {
      this.trades = trades;
      this.cdr.detectChanges();
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
