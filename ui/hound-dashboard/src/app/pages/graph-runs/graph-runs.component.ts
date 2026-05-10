import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { GraphRun, NodeSnapshot, NodeStatus } from '../../models';

@Component({
  selector: 'app-graph-runs',
  standalone: true,
  imports: [CommonModule, FormsModule],
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

  readonly nodeLabels: Record<string, string> = {
    'data-node': 'Data Analysis',
    'strategy-node': 'Strategy',
    'risk-node': 'Risk Assessment',
    'execution-node': 'Execution',
    'monitor-node': 'Monitor',
  };

  readonly nodeDescriptions: Record<string, string> = {
    'data-node': 'Fetches market data and computes technical indicators',
    'strategy-node': 'Formulates a trading decision based on analysis',
    'risk-node': 'Evaluates risk limits and validates the trade',
    'execution-node': 'Places the order via Alpaca Markets',
    'monitor-node': 'Monitors the position until the trade closes',
  };

  constructor(
    private api: ApiService,
    private signalr: SignalrService,
    private cdr: ChangeDetectorRef,
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
