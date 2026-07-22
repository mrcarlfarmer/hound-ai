import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, HostListener, Input, OnChanges, OnDestroy, SimpleChanges, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  CandlestickSeries,
  HistogramSeries,
  createChart,
  type CandlestickData,
  type HistogramData,
  type IChartApi,
  type ISeriesApi,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import { ApiService } from '../../services/api.service';
import type { BarPoint, BarsResponse, ChartSnapshot, ChartTimeframe } from '../../models';

/**
 * Renders OHLCV bar data for a single ticker using TradingView's
 * lightweight-charts library. Pulls bars from `GET /api/charts/{symbol}`,
 * which is proxied through the trading pack — see `ChartsController` /
 * `IMarketDataClient`.
 *
 * Designed to be embeddable next to analyst output on the run / pack detail
 * pages, so the component owns its own loading + error UI but accepts the
 * symbol and timeframe via inputs.
 */
@Component({
  selector: 'app-chart-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './chart-panel.component.html',
  // Angular components default to display:inline, which collapses any
  // child using h-full to 0px. Force the host to behave like a block-level
  // flex container so the chart canvas can actually pick up its parent's
  // height (e.g. when embedded in `h-[480px]` containers).
  styles: [':host { display: flex; flex-direction: column; width: 100%; height: 100%; min-height: 0; }'],
})
export class ChartPanelComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input({ required: true }) symbol!: string;
  @Input() timeframe: ChartTimeframe = '1Day';
  @Input() days = 90;
  /** When true, hides the inline timeframe selector and uses the input value verbatim. */
  @Input() hideControls = false;
  /**
   * Optional pre-fetched snapshot to render instead of calling the API.
   * Used by the analyst-team Chart tab to show the exact bars captured
   * during the run — even days later when live data has moved on. When
   * set, timeframe/window controls are hidden and live-refresh is disabled.
   */
  @Input() snapshot: ChartSnapshot | null = null;

  @ViewChild('chartContainer', { static: true }) chartContainer!: ElementRef<HTMLDivElement>;

  readonly availableTimeframes: ChartTimeframe[] = ['15Min', '1Hour', '1Day', '1Week', '1Month'];
  readonly availableWindows: { label: string; days: number }[] = [
    { label: '7D', days: 7 },
    { label: '30D', days: 30 },
    { label: '90D', days: 90 },
    { label: '1Y', days: 365 },
    { label: '5Y', days: 365 * 5 },
  ];

  loading = false;
  errorMessage: string | null = null;
  lastBarCount = 0;
  lastUpdated: Date | null = null;
  /** True when the chart is rendering a persisted snapshot rather than live data. */
  isSnapshot = false;

  private chart: IChartApi | null = null;
  private candleSeries: ISeriesApi<'Candlestick'> | null = null;
  private volumeSeries: ISeriesApi<'Histogram'> | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private viewInitialised = false;
  /**
   * Identity key of the snapshot last applied to the chart. Used by
   * {@link refreshData} to short-circuit redundant work when the parent
   * reassigns its run object (e.g. on SignalR pushes or 10s polls). Snapshots
   * are immutable per run, so symbol + capturedAt is a sufficient identity.
   */
  private lastSnapshotKey: string | null = null;
  /**
   * Identity key of the last live-fetch performed. Same purpose as
   * {@link lastSnapshotKey} but for the API-fetch path; tuple of
   * symbol + timeframe + days. Cleared when the user explicitly clicks
   * Refresh / changes timeframe so the next call always re-fetches.
   */
  private lastLiveKey: string | null = null;

  constructor(private readonly api: ApiService, private readonly host: ElementRef<HTMLElement>) {}

  ngAfterViewInit(): void {
    this.viewInitialised = true;
    this.initChart();
    this.refreshData();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.viewInitialised) return;
    // refreshData is idempotent via lastSnapshotKey / lastLiveKey, so it's
    // safe to call on any input change — it will short-circuit when nothing
    // meaningful has actually changed (e.g. parent reassigning the run on a
    // SignalR push that doesn't touch the chart data).
    if (changes['snapshot'] || changes['symbol'] || changes['timeframe'] || changes['days']) {
      this.refreshData();
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.chart?.remove();
    this.chart = null;
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.fitToContainer();
  }

  setTimeframe(tf: ChartTimeframe): void {
    if (tf === this.timeframe) return;
    this.timeframe = tf;
    this.lastLiveKey = null;
    this.loadBars();
  }

  setWindow(days: number): void {
    if (days === this.days) return;
    this.days = days;
    this.lastLiveKey = null;
    this.loadBars();
  }

  refresh(): void {
    this.lastLiveKey = null;
    this.loadBars();
  }

  /**
   * Routes data loading between the persisted snapshot path (no HTTP) and
   * the live API path. Both paths track an identity key (symbol + capturedAt
   * for snapshots, symbol + timeframe + days for live) and bail out if the
   * exact same load has already been performed. This is what guarantees the
   * stated invariant: "chart loads fresh data once during the analyst phase,
   * once from the store for old runs."
   *
   * Parents like graph-runs reassign `selectedRun` on every SignalR push
   * and every 10s poll — those reassignments hit this method on every
   * cycle, and the identity-key short-circuit is what stops them from
   * triggering pointless re-renders / re-fetches.
   */
  private refreshData(): void {
    if (this.snapshot) {
      const key = `${this.snapshot.symbol}|${this.snapshot.capturedAt}`;
      if (key === this.lastSnapshotKey) return;
      this.lastSnapshotKey = key;
      this.lastLiveKey = null;
      this.applySnapshot(this.snapshot);
      return;
    }
    if (this.symbol) {
      const key = `${this.symbol}|${this.timeframe}|${this.days}`;
      if (key === this.lastLiveKey) return;
      this.lastLiveKey = key;
      this.lastSnapshotKey = null;
      this.loadBars();
    }
  }

  private applySnapshot(snapshot: ChartSnapshot): void {
    this.isSnapshot = true;
    this.loading = false;
    this.errorMessage = null;
    this.applyBars({
      symbol: snapshot.symbol,
      timeframe: snapshot.timeframe,
      from: snapshot.from,
      to: snapshot.to,
      bars: snapshot.bars,
    });
    this.lastUpdated = new Date(snapshot.capturedAt);
  }

  private initChart(): void {
    const container = this.chartContainer.nativeElement;
    const chart = createChart(container, {
      autoSize: true,
      layout: {
        background: { color: 'transparent' },
        textColor: '#9ca3af',
      },
      grid: {
        vertLines: { color: 'rgba(148, 163, 184, 0.12)' },
        horzLines: { color: 'rgba(148, 163, 184, 0.12)' },
      },
      crosshair: { mode: 1 },
      rightPriceScale: { borderColor: 'rgba(148, 163, 184, 0.25)' },
      timeScale: {
        borderColor: 'rgba(148, 163, 184, 0.25)',
        timeVisible: true,
        secondsVisible: false,
      },
    });

    this.chart = chart;
    this.candleSeries = chart.addSeries(CandlestickSeries, {
      upColor: '#22c55e',
      downColor: '#ef4444',
      borderUpColor: '#22c55e',
      borderDownColor: '#ef4444',
      wickUpColor: '#22c55e',
      wickDownColor: '#ef4444',
    });

    this.volumeSeries = chart.addSeries(HistogramSeries, {
      priceFormat: { type: 'volume' },
      priceScaleId: 'volume',
      color: 'rgba(148, 163, 184, 0.45)',
    });
    // Park volume in the bottom 20% of the pane so it doesn't fight candles.
    chart.priceScale('volume').applyOptions({
      scaleMargins: { top: 0.8, bottom: 0 },
    });

    this.resizeObserver = new ResizeObserver(() => this.fitToContainer());
    this.resizeObserver.observe(container);
  }

  private fitToContainer(): void {
    if (!this.chart) return;
    const container = this.chartContainer.nativeElement;
    this.chart.applyOptions({ width: container.clientWidth, height: container.clientHeight });
  }

  private loadBars(): void {
    if (!this.symbol) return;
    this.isSnapshot = false;
    this.loading = true;
    this.errorMessage = null;

    this.api.getBars(this.symbol, this.timeframe, this.days).subscribe({
      next: (response) => {
        this.applyBars(response);
        this.loading = false;
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err?.error?.detail || err?.message || 'Failed to load bars';
        this.candleSeries?.setData([]);
        this.volumeSeries?.setData([]);
      },
    });
  }

  private applyBars(response: BarsResponse): void {
    if (!this.candleSeries || !this.volumeSeries) return;

    const candles: CandlestickData<Time>[] = [];
    const volumes: HistogramData<Time>[] = [];

    for (const bar of response.bars) {
      const time = toUtcTimestamp(bar);
      if (time === null) continue;

      candles.push({
        time,
        open: bar.open,
        high: bar.high,
        low: bar.low,
        close: bar.close,
      });
      volumes.push({
        time,
        value: bar.volume,
        color: bar.close >= bar.open ? 'rgba(34, 197, 94, 0.45)' : 'rgba(239, 68, 68, 0.45)',
      });
    }

    this.candleSeries.setData(candles);
    this.volumeSeries.setData(volumes);
    this.chart?.timeScale().fitContent();

    this.lastBarCount = candles.length;
    // Snapshot path overrides `lastUpdated` with the captured timestamp
    // immediately after this call returns.
    if (!this.isSnapshot) {
      this.lastUpdated = new Date();
    }
  }
}

function toUtcTimestamp(bar: BarPoint): UTCTimestamp | null {
  const ms = Date.parse(bar.time);
  if (Number.isNaN(ms)) return null;
  return Math.floor(ms / 1000) as UTCTimestamp;
}
