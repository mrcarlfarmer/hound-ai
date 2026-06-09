import { ChangeDetectionStrategy, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ChartPanelComponent } from '../../components/chart-panel/chart-panel.component';
import type { ChartTimeframe } from '../../models';

/**
 * Standalone charts explorer. Lets the user type any ticker and inspect its
 * recent OHLCV history alongside the analyst pipeline output. The same
 * `<app-chart-panel>` can be embedded on `pack-detail` or `graph-runs` once
 * we want the chart sitting next to a hound's analysis.
 */
@Component({
  selector: 'app-charts-page',
  standalone: true,
  imports: [CommonModule, FormsModule, ChartPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './charts.component.html',
})
export class ChartsComponent implements OnInit {
  symbol = 'AAPL';
  timeframe: ChartTimeframe = '1Day';
  days = 90;
  // Used by the chart panel; bumped whenever the user submits a new symbol so
  // the OnPush component sees a fresh @Input.
  activeSymbol = this.symbol;

  constructor(private readonly route: ActivatedRoute, private readonly router: Router) {}

  ngOnInit(): void {
    const queryParam = this.route.snapshot.queryParamMap.get('symbol');
    if (queryParam) {
      this.symbol = queryParam.toUpperCase();
      this.activeSymbol = this.symbol;
    }
  }

  submit(): void {
    const next = (this.symbol ?? '').trim().toUpperCase();
    if (!next || next === this.activeSymbol) return;
    this.symbol = next;
    this.activeSymbol = next;
    // Keep the URL shareable.
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { symbol: next },
      queryParamsHandling: 'merge',
    });
  }
}
