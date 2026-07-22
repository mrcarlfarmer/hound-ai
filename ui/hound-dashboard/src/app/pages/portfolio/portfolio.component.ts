import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { AccountSummary, PositionInfo } from '../../models';

@Component({
  selector: 'app-portfolio',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './portfolio.component.html',
})
export class PortfolioComponent implements OnInit, OnDestroy {
  account: AccountSummary | null = null;
  positions: PositionInfo[] = [];
  loading = true;
  error: string | null = null;
  closingSymbol: string | null = null;
  private refreshInterval: ReturnType<typeof setInterval> | null = null;

  constructor(private api: ApiService, private cdr: ChangeDetectorRef) {}

  ngOnInit(): void {
    this.loadData();
    this.refreshInterval = setInterval(() => this.loadData(), 30000);
  }

  ngOnDestroy(): void {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }
  }

  loadData(): void {
    this.api.getAccount().subscribe({
      next: (account) => {
        this.account = account;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = 'Failed to load account data';
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
    this.api.getPositions().subscribe({
      next: (positions) => {
        this.positions = positions;
        this.cdr.detectChanges();
      },
      error: () => {},
    });
  }

  totalMarketValue(): number {
    return this.positions.reduce((sum, p) => sum + p.marketValue, 0);
  }

  totalUnrealizedPl(): number {
    return this.positions.reduce((sum, p) => sum + p.unrealizedPl, 0);
  }

  plClass(value: number): Record<string, boolean> {
    return {
      'text-green-600': value > 0,
      'text-red-600': value < 0,
      'text-muted-foreground': value === 0,
    };
  }

  closePosition(symbol: string): void {
    if (this.closingSymbol) return;
    this.closingSymbol = symbol;
    this.api.closePosition(symbol).subscribe({
      next: () => {
        this.closingSymbol = null;
        this.loadData();
      },
      error: () => {
        this.closingSymbol = null;
        this.cdr.detectChanges();
      },
    });
  }
}
