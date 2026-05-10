import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Subscription } from 'rxjs';
import { Marked } from 'marked';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { GraphRun, NodeSnapshot, NodeStatus } from '../../models';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';

interface AnalystsOutput {
  symbol?: string;
  lastPrice?: number;
  volumeChange?: number;
  trend?: string;
  confidenceScore?: number;
  summary?: string;
  indicators?: Record<string, unknown>;
  marketReport?: string;
  fundamentalsReport?: string;
  newsReport?: string;
  sentimentReport?: string;
}

interface StrategyOutput {
  symbol?: string;
  action?: string;
  quantity?: number;
  reasoning?: string;
  confidence?: number;
}

@Component({
  selector: 'app-graph-runs',
  standalone: true,
  imports: [CommonModule, FormsModule, ...HlmTabsImports],
  templateUrl: './graph-runs.component.html',
  styles: []
})
export class GraphRunsComponent implements OnInit, OnDestroy {
  runs: GraphRun[] = [];
  selectedRun?: GraphRun;
  loading = false;
  expandedNodes = new Set<string>();
  symbolInput = '';
  submitting = false;
  submitError = '';
  private sub?: Subscription;
  private pollTimer?: ReturnType<typeof setInterval>;
  private marked = new Marked({ gfm: true, async: false });

  readonly nodeLabels: Record<string, string> = {
    'analysts-team-node': 'Analysts Team',
    'strategy-node': 'Strategy',
    'risk-node': 'Risk Assessment',
    'execution-node': 'Execution',
    'monitor-node': 'Monitor',
  };

  readonly nodeDescriptions: Record<string, string> = {
    'analysts-team-node': 'Market, fundamentals, news, and sentiment analysis',
    'strategy-node': 'Formulates a trading decision based on analysis',
    'risk-node': 'Evaluates risk limits and validates the trade',
    'execution-node': 'Places the order via Alpaca Markets',
    'monitor-node': 'Monitors the position until the trade closes',
  };

  constructor(
    private api: ApiService,
    private signalr: SignalrService,
    private cdr: ChangeDetectorRef,
    private sanitizer: DomSanitizer,
  ) {}

  ngOnInit(): void {
    this.loading = true;
    this.loadRuns();

    this.signalr.connect();
    this.signalr.subscribeToPack('trading-pack');
    this.sub = this.signalr.onGraphRunUpdate$.subscribe(run => {
      this.mergeRun(run);
      if (this.selectedRun?.runId === run.runId) {
        this.selectedRun = run;
      }
      this.cdr.detectChanges();
    });

    // Poll for updates every 10s (covers runs that started while page was open)
    this.pollTimer = setInterval(() => this.loadRuns(), 10000);
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.signalr.unsubscribeFromPack('trading-pack');
    if (this.pollTimer) clearInterval(this.pollTimer);
  }

  submitSymbol(): void {
    const symbol = this.symbolInput.trim().toUpperCase();
    if (!symbol) return;

    this.submitting = true;
    this.submitError = '';
    this.api.queueRun(symbol).subscribe({
      next: () => {
        this.symbolInput = '';
        this.submitting = false;
        this.cdr.detectChanges();
        // Refresh runs list after a short delay to pick up the new run
        setTimeout(() => this.loadRuns(), 2000);
      },
      error: (err) => {
        this.submitError = err.error?.message || err.message || 'Failed to queue run';
        this.submitting = false;
        this.cdr.detectChanges();
      },
    });
  }

  loadRuns(): void {
    this.api.getRuns(20).subscribe({
      next: runs => {
        this.runs = runs;
        if (!this.selectedRun && runs.length > 0) {
          this.selectRun(runs[0]);
        }
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
  }

  selectRun(run: GraphRun): void {
    this.selectedRun = run;
    this.expandedNodes.clear();
    // Auto-expand completed nodes
    run.nodes?.forEach(n => {
      if (n.status === 'Completed' || n.status === 'Failed') {
        this.expandedNodes.add(n.nodeId);
      }
    });
    this.cdr.detectChanges();
  }

  toggleNode(nodeId: string): void {
    if (this.expandedNodes.has(nodeId)) {
      this.expandedNodes.delete(nodeId);
    } else {
      this.expandedNodes.add(nodeId);
    }
  }

  isExpanded(nodeId: string): boolean {
    return this.expandedNodes.has(nodeId);
  }

  parseOutput(json?: string): Record<string, unknown> | null {
    if (!json) return null;
    try {
      return JSON.parse(json);
    } catch {
      return null;
    }
  }

  isObject(value: unknown): boolean {
    return typeof value === 'object' && value !== null;
  }

  renderMarkdown(text: string | null | undefined): SafeHtml {
    if (!text) return '';
    const html = this.marked.parse(text) as string;
    return this.sanitizer.bypassSecurityTrustHtml(html);
  }

  parseAnalystsOutput(json?: string): AnalystsOutput | null {
    if (!json) return null;
    try {
      return JSON.parse(json) as AnalystsOutput;
    } catch {
      return null;
    }
  }

  trendClass(trend?: string): string {
    const t = trend?.toLowerCase() ?? '';
    if (t.includes('bullish')) return 'bg-green-900/40 text-green-400 border-green-600';
    if (t.includes('bearish')) return 'bg-red-900/40 text-red-400 border-red-600';
    return 'bg-yellow-900/40 text-yellow-400 border-yellow-600';
  }

  private normalizeConfidence(score?: number): number {
    const s = score ?? 0;
    // LLM returns values on different scales: 0-1, 1-10, or 0-100
    if (s > 10) return Math.min(s / 100, 1);
    if (s > 1) return Math.min(s / 10, 1);
    return Math.max(0, Math.min(s, 1));
  }

  confidenceWidth(score?: number): string {
    return `${Math.round(this.normalizeConfidence(score) * 100)}%`;
  }

  confidenceColor(score?: number): string {
    const s = this.normalizeConfidence(score);
    if (s >= 0.7) return 'bg-green-500';
    if (s >= 0.4) return 'bg-yellow-500';
    return 'bg-red-500';
  }

  confidencePercent(score?: number): string {
    if (score === undefined) return '—';
    return `${Math.round(this.normalizeConfidence(score) * 100)}%`;
  }

  hasAnalystReports(output: AnalystsOutput): boolean {
    return !!(output.marketReport || output.fundamentalsReport || output.newsReport || output.sentimentReport);
  }

  parseStrategyOutput(json?: string): StrategyOutput | null {
    if (!json) return null;
    try {
      return JSON.parse(json) as StrategyOutput;
    } catch {
      return null;
    }
  }

  actionClass(action?: string): string {
    switch (action?.toLowerCase()) {
      case 'buy': return 'bg-green-900/40 text-green-400 border-green-600';
      case 'sell': return 'bg-red-900/40 text-red-400 border-red-600';
      default: return 'bg-yellow-900/40 text-yellow-400 border-yellow-600';
    }
  }

  private readonly _downstreamNodes = new Set(['risk-node', 'execution-node', 'monitor-node']);

  isNoAction(node: NodeSnapshot): boolean {
    if (node.status !== 'Pending' || !this._downstreamNodes.has(node.nodeId)) return false;
    const strategyNode = this.selectedRun?.nodes?.find(n => n.nodeId === 'strategy-node');
    if (!strategyNode || strategyNode.status !== 'Completed') return false;
    const s = this.parseStrategyOutput(strategyNode.outputJson);
    return !!s && s.action?.toLowerCase() !== 'buy';
  }

  displayStatus(node: NodeSnapshot): string {
    return this.isNoAction(node) ? 'No action' : node.status;
  }

  nodeStatusClass(status: NodeStatus): string {
    switch (status) {
      case 'Active': return 'bg-blue-900/40 text-blue-400 border-blue-500';
      case 'Completed': return 'bg-green-900/40 text-green-400 border-green-500';
      case 'Failed': return 'bg-red-900/40 text-red-400 border-red-500';
      case 'Skipped': return 'bg-gray-900/40 text-gray-400 border-gray-500';
      default: return 'bg-muted text-muted-foreground border-border';
    }
  }

  nodeConnectorClass(status: NodeStatus): string {
    switch (status) {
      case 'Active': return 'bg-blue-500';
      case 'Completed': return 'bg-green-500';
      case 'Failed': return 'bg-red-500';
      default: return 'bg-border';
    }
  }

  statusBadgeClass(run: GraphRun): string {
    if (run.errorMessage) return 'bg-red-900/40 text-red-400';
    if (run.isComplete) return 'bg-green-900/40 text-green-400';
    return 'bg-blue-900/40 text-blue-400';
  }

  statusLabel(run: GraphRun): string {
    if (run.errorMessage) return 'Error';
    if (run.isComplete) return 'Complete';
    return 'Running';
  }

  formatTime(iso?: string): string {
    if (!iso) return '—';
    const d = new Date(iso);
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  formatDate(iso?: string): string {
    if (!iso) return '—';
    const d = new Date(iso);
    return d.toLocaleDateString([], { month: 'short', day: 'numeric' }) + ' ' + this.formatTime(iso);
  }

  private mergeRun(run: GraphRun): void {
    const idx = this.runs.findIndex(r => r.runId === run.runId);
    if (idx >= 0) {
      this.runs[idx] = run;
    } else {
      this.runs.unshift(run);
    }
  }
}
