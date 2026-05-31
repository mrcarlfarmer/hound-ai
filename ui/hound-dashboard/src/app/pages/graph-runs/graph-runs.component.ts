import { Component, OnInit, OnDestroy, AfterViewInit, ChangeDetectorRef, ViewChildren, QueryList, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Subscription } from 'rxjs';
import { Marked } from 'marked';
import DOMPurify from 'dompurify';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { GraphRun, NodeSnapshot, NodeStatus, NodeStreamChunk, RunRequest } from '../../models';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';

interface AnalystsOutput {
  symbol?: string;
  companyName?: string;
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

interface RiskOutput {
  verdict?: string;
  decision?: {
    symbol?: string;
    action?: string;
    quantity?: number;
    confidence?: number;
  };
  reasoning?: string;
  adjustedQuantity?: number | null;
}

interface ExecutionOutput {
  success?: boolean;
  symbol?: string;
  action?: string;
  quantity?: number;
  filledPrice?: number | null;
  orderId?: string;
  message?: string;
  tradeDocumentId?: string;
}

interface MonitorOutput {
  tradeOpen?: boolean;
  currentStatus?: string;
  currentPrice?: number | null;
  unrealizedPnL?: number | null;
  summary?: string;
}

@Component({
  selector: 'app-graph-runs',
  standalone: true,
  imports: [CommonModule, FormsModule, ...HlmTabsImports],
  templateUrl: './graph-runs.component.html',
  styles: []
})
export class GraphRunsComponent implements OnInit, OnDestroy, AfterViewInit {
  runs: GraphRun[] = [];
  pendingRequests: RunRequest[] = [];
  selectedRun?: GraphRun;
  loading = false;
  expandedNodes = new Set<string>();
  /** Live-streamed reasoning text, keyed by `${runId}:${nodeId}`. */
  nodeStreams = new Map<string, string>();
  /** Currently active tab per node, keyed by nodeId. */
  activeTab = new Map<string, 'result' | 'reasoning'>();
  symbolInput = '';
  submitting = false;
  submitError = '';
  closingPosition = false;
  closePositionError = '';
  // Human-in-the-loop approval UI state
  approvalNotes = '';
  approvalSubmitting: 'approve' | 'reject' | null = null;
  approvalError = '';
  private sub?: Subscription;
  private streamSub?: Subscription;
  private pollTimer?: ReturnType<typeof setInterval>;
  private marked = new Marked({ gfm: true, async: false });
  /** Reasoning <pre> elements, used to auto-scroll as chunks arrive. */
  @ViewChildren('reasoningBox') private reasoningBoxes?: QueryList<ElementRef<HTMLElement>>;

  readonly nodeLabels: Record<string, string> = {
    'analysts-team-node': 'Analysts Team',
    'strategy-node': 'Strategy',
    'risk-node': 'Risk Assessment',
    'approval-node': 'Human Approval',
    'execution-node': 'Execution',
    'monitor-node': 'Monitor',
  };

  readonly nodeDescriptions: Record<string, string> = {
    'analysts-team-node': 'Market, fundamentals, news, and sentiment analysis',
    'strategy-node': 'Formulates a trading decision based on analysis',
    'risk-node': 'Evaluates risk limits and validates the trade',
    'approval-node': 'Pauses for a human to review and approve or reject the trade',
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
        this.autoExpandActive(run);
      }
      this.cdr.detectChanges();
    });
    this.streamSub = this.signalr.onNodeStream$.subscribe(chunk => this.appendStream(chunk));

    // Poll for updates every 10s (covers runs that started while page was open)
    this.pollTimer = setInterval(() => this.loadRuns(), 10000);
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.streamSub?.unsubscribe();
    this.viewChildrenSub?.unsubscribe();
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
    this.api.getRunRequests(10).subscribe({
      next: requests => {
        // Show requests that are Pending or Running and don't yet have a GraphRun
        this.pendingRequests = requests.filter(r =>
          (r.status === 'Pending' || r.status === 'Running') &&
          !this.runs.some(run => run.runId === r.runId)
        );
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
    this.autoExpandActive(run);
    this.cdr.detectChanges();
  }

  /** Expand the active node and default it to the reasoning tab. */
  private autoExpandActive(run: GraphRun): void {
    run.nodes?.forEach(n => {
      if (n.status === 'Active') {
        this.expandedNodes.add(n.nodeId);
        if (!this.activeTab.has(n.nodeId)) {
          this.activeTab.set(n.nodeId, 'reasoning');
        }
      } else if (n.status === 'Completed' && this.activeTab.get(n.nodeId) === 'reasoning') {
        // Once the node has a result, flip back to the result tab
        this.activeTab.set(n.nodeId, 'result');
      }
    });
  }

  private appendStream(chunk: NodeStreamChunk): void {
    const key = `${chunk.runId}:${chunk.nodeId}`;
    const current = this.nodeStreams.get(key) ?? '';
    this.nodeStreams.set(key, current + chunk.text);
    if (this.selectedRun?.runId === chunk.runId) {
      this.cdr.detectChanges();
    }
  }

  // ── Auto-scroll plumbing ─────────────────────────────────────────────────
  //
  // Each reasoning <pre> has a fixed max-height (max-h-96) + overflow-y-auto,
  // so its border-box size NEVER changes — ResizeObserver wouldn't fire. We
  // attach a MutationObserver to each box's subtree to catch every text-change
  // and snap scrollTop to scrollHeight. A scroll listener tracks whether the
  // user has scrolled away from the bottom; if so, auto-scroll is paused until
  // they return to within STICKY_THRESHOLD_PX of the bottom.
  private viewChildrenSub?: Subscription;
  /** Elements currently observed, with their per-box observer + cleanup state. */
  private observed = new WeakMap<HTMLElement, {
    mutationObserver: MutationObserver;
    onScroll: () => void;
    stickToBottom: boolean;
  }>();
  /** Pixel tolerance for considering the user "at the bottom". */
  private static readonly STICKY_THRESHOLD_PX = 24;

  ngAfterViewInit(): void {
    if (typeof MutationObserver === 'undefined' || !this.reasoningBoxes) {
      return;
    }
    this.syncObservedBoxes(this.reasoningBoxes.toArray());
    this.viewChildrenSub = this.reasoningBoxes.changes.subscribe(
      (list: QueryList<ElementRef<HTMLElement>>) => this.syncObservedBoxes(list.toArray())
    );
  }

  private syncObservedBoxes(refs: ElementRef<HTMLElement>[]): void {
    for (const ref of refs) {
      const el = ref.nativeElement;
      if (this.observed.has(el)) continue;

      const state = {
        mutationObserver: null as unknown as MutationObserver,
        onScroll: () => { /* set below */ },
        stickToBottom: true,
      };
      state.onScroll = () => {
        const distanceFromBottom = el.scrollHeight - el.clientHeight - el.scrollTop;
        state.stickToBottom = distanceFromBottom <= GraphRunsComponent.STICKY_THRESHOLD_PX;
      };
      const mo = new MutationObserver(() => {
        if (state.stickToBottom) {
          el.scrollTop = el.scrollHeight;
        }
      });
      mo.observe(el, { childList: true, characterData: true, subtree: true });
      state.mutationObserver = mo;

      el.addEventListener('scroll', state.onScroll, { passive: true });
      this.observed.set(el, state);
      // Snap to bottom on initial attach so freshly opened tabs start tailed.
      el.scrollTop = el.scrollHeight;
    }
    // Detached elements get GC'd along with their WeakMap entries when
    // Angular destroys them; their MutationObservers stop firing automatically.
  }

  streamFor(node: NodeSnapshot): string {
    if (!this.selectedRun) return node.reasoningText ?? '';
    const live = this.nodeStreams.get(`${this.selectedRun.runId}:${node.nodeId}`) ?? '';
    // Prefer live stream while running; fall back to persisted reasoning so it
    // survives page reloads after the run completes.
    return live.length > 0 ? live : (node.reasoningText ?? '');
  }

  hasReasoning(node: NodeSnapshot): boolean {
    return this.streamFor(node).length > 0;
  }

  tabFor(node: NodeSnapshot): 'result' | 'reasoning' {
    return this.activeTab.get(node.nodeId)
      ?? (node.status === 'Active' ? 'reasoning' : 'result');
  }

  setTab(node: NodeSnapshot, tab: 'result' | 'reasoning'): void {
    this.activeTab.set(node.nodeId, tab);
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
    const rawHtml = this.marked.parse(text) as string;
    // Marked can emit raw HTML from the source markdown, so sanitize before
    // bypassing Angular's built-in sanitizer. DOMPurify strips scripts,
    // javascript: URLs, inline event handlers, and other XSS vectors.
    const cleanHtml = DOMPurify.sanitize(rawHtml, {
      USE_PROFILES: { html: true },
      FORBID_TAGS: ['style', 'iframe', 'object', 'embed', 'form'],
      FORBID_ATTR: ['style', 'onerror', 'onload', 'onclick'],
    });
    return this.sanitizer.bypassSecurityTrustHtml(cleanHtml);
  }

  parseAnalystsOutput(json?: string): AnalystsOutput | null {
    if (!json) return null;
    try {
      return JSON.parse(json) as AnalystsOutput;
    } catch {
      return null;
    }
  }

  /**
   * Resolves the canonical company name for a run by inspecting the analysts
   * team node's output. Returns null until the analysts node finishes.
   */
  companyNameFor(run?: GraphRun | null): string | null {
    const analysts = run?.nodes?.find(n => n.nodeId === 'analysts-team-node');
    const parsed = this.parseAnalystsOutput(analysts?.outputJson);
    return parsed?.companyName?.trim() || null;
  }

  trendClass(trend?: string): string {
    const t = trend?.toLowerCase() ?? '';
    if (t.includes('bullish')) return 'bg-green-900/40 text-green-400 border-green-600';
    if (t.includes('bearish')) return 'bg-red-900/40 text-red-400 border-red-600';
    return 'bg-yellow-900/40 text-yellow-400 border-yellow-600';
  }

  /**
   * Collapses any free-form trend label produced by the LLM into one of the
   * three canonical values — Bullish / Bearish / Neutral — so the badge stays
   * compact even when the model returns something like
   * "moderately_bullish_with_regulatory_concerns".
   */
  formatTrend(trend?: string): string {
    const t = trend?.toLowerCase() ?? '';
    if (t.includes('bull')) return 'Bullish';
    if (t.includes('bear')) return 'Bearish';
    if (!trend) return '—';
    return 'Neutral';
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

  parseRiskOutput(json?: string): RiskOutput | null {
    if (!json) return null;
    try {
      return JSON.parse(json) as RiskOutput;
    } catch {
      return null;
    }
  }

  verdictClass(verdict?: string): string {
    switch (verdict?.toLowerCase()) {
      case 'approved': return 'bg-green-900/40 text-green-400 border-green-600';
      case 'rejected': return 'bg-red-900/40 text-red-400 border-red-600';
      case 'modified': return 'bg-yellow-900/40 text-yellow-400 border-yellow-600';
      default: return 'bg-muted text-muted-foreground border-border';
    }
  }

  parseExecutionOutput(json?: string): ExecutionOutput | null {
    if (!json) return null;
    try {
      return JSON.parse(json) as ExecutionOutput;
    } catch {
      return null;
    }
  }

  parseMonitorOutput(json?: string): MonitorOutput | null {
    if (!json) return null;
    try {
      return JSON.parse(json) as MonitorOutput;
    } catch {
      return null;
    }
  }

  monitorStatusClass(status?: string): string {
    switch (status?.toLowerCase()) {
      case 'filled': return 'bg-green-900/40 text-green-400 border-green-600';
      case 'partiallyfilled': return 'bg-blue-900/40 text-blue-400 border-blue-600';
      case 'pending': return 'bg-yellow-900/40 text-yellow-400 border-yellow-600';
      case 'canceled':
      case 'expired':
      case 'rejected': return 'bg-red-900/40 text-red-400 border-red-600';
      default: return 'bg-muted text-muted-foreground border-border';
    }
  }

  pnlClass(pnl?: number | null): string {
    if (pnl == null) return 'text-muted-foreground';
    return pnl >= 0 ? 'text-green-400' : 'text-red-400';
  }

  closePositionFromMonitor(): void {
    const symbol = this.selectedRun?.symbol;
    if (!symbol || this.closingPosition) return;
    this.closingPosition = true;
    this.closePositionError = '';
    this.api.closePosition(symbol).subscribe({
      next: () => {
        this.closingPosition = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.closingPosition = false;
        this.closePositionError = 'Failed to close position';
        this.cdr.detectChanges();
      },
    });
  }

  executionStatusClass(output: ExecutionOutput): string {
    if (output.success === false) return 'bg-red-900/40 text-red-400 border-red-600';
    if (output.message?.toLowerCase().includes('filled')) return 'bg-green-900/40 text-green-400 border-green-600';
    return 'bg-yellow-900/40 text-yellow-400 border-yellow-600';
  }

  executionStatusLabel(output: ExecutionOutput): string {
    if (output.success === false) return 'Failed';
    if (output.message?.toLowerCase().includes('filled')) return 'Filled';
    return 'Submitted';
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
    if (this.isNoAction(node)) return 'No action';
    if (node.status === 'Pending' && this.selectedRun?.isComplete) return 'Skipped';
    return node.status;
  }

  // ── Human-in-the-loop approval ────────────────────────────────────────────

  isAwaitingApproval(run?: GraphRun | null): boolean {
    return !!run && !run.isComplete && run.approvalStatus === 'Pending';
  }

  submitApproval(decision: 'approve' | 'reject'): void {
    if (!this.selectedRun || this.approvalSubmitting) return;
    this.approvalSubmitting = decision;
    this.approvalError = '';

    const runId = this.selectedRun.runId;
    const notes = this.approvalNotes.trim() || undefined;
    const call = decision === 'approve'
      ? this.api.approveRun(runId, 'dashboard-user', notes)
      : this.api.rejectRun(runId, 'dashboard-user', notes);

    call.subscribe({
      next: () => {
        this.approvalSubmitting = null;
        this.approvalNotes = '';
        // Refresh — the worker will pick up the decision on its next poll
        // and SignalR will push the new run state shortly after.
        this.loadRuns();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.approvalSubmitting = null;
        const problem = (err as { error?: { title?: string; detail?: string } })?.error;
        this.approvalError = problem?.detail
          ?? problem?.title
          ?? (err as { message?: string })?.message
          ?? 'Failed to submit decision';
        this.cdr.detectChanges();
      },
    });
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
    if (run.phase === 'Monitor') return 'bg-amber-900/40 text-amber-400';
    return 'bg-blue-900/40 text-blue-400';
  }

  statusLabel(run: GraphRun): string {
    if (run.errorMessage) return 'Error';
    if (run.isComplete) return 'Complete';
    if (run.phase === 'Monitor') return 'Monitoring';
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
