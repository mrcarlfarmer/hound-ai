import { Component, OnInit, OnDestroy, AfterViewInit, ChangeDetectorRef, ViewChildren, QueryList, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Subscription } from 'rxjs';
import { Marked } from 'marked';
import DOMPurify from 'dompurify';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { GraphRun, NodeSnapshot, NodeStatus, NodeStreamChunk, RunRequest, DebateRecord, DebateTurnSnapshot } from '../../models';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { ChartPanelComponent } from '../../components/chart-panel/chart-panel.component';

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
  currentPrice?: number | null;
  estimatedCost?: number | null;
  trailPercent?: number | null;
}

interface RiskOutput {
  verdict?: string;
  decision?: {
    symbol?: string;
    action?: string;
    quantity?: number;
    confidence?: number;
    currentPrice?: number | null;
    estimatedCost?: number | null;
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
  imports: [CommonModule, FormsModule, ChartPanelComponent, ...HlmTabsImports],
  templateUrl: './graph-runs.component.html',
  styles: []
})
export class GraphRunsComponent implements OnInit, OnDestroy, AfterViewInit {
  runs: GraphRun[] = [];
  pendingRequests: RunRequest[] = [];
  selectedRun?: GraphRun;
  /**
   * Debate transcript(s) for the selected run, fetched from
   * `/api/debates/{runId}`. One record per StrategyNode invocation.
   */
  debateRecords: DebateRecord[] = [];
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
      const isNew = this.mergeRun(run);
      if (isNew) {
        // A brand-new run just started — pull it into focus so the user
        // can watch it execute without manually clicking it in the sidebar.
        this.selectRun(run);
      } else if (this.selectedRun?.runId === run.runId) {
        this.selectedRun = run;
        this.autoExpandActive(run);
        // The debate is persisted while StrategyNode runs; if we selected the
        // run before that completed, pick it up on the next update.
        if (this.debateRecords.length === 0) {
          this.loadDebates(run.runId);
        }
      }
      this.maybeClearApprovalSubmitting(run);
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

  onSymbolInput(): void {
    this.symbolInput = this.symbolInput.toUpperCase();
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
        } else if (this.selectedRun) {
          // Keep the in-memory selectedRun in sync with the polled snapshot —
          // notably so the approval-submit flag clears even if a SignalR push
          // was missed.
          const fresh = runs.find(r => r.runId === this.selectedRun!.runId);
          if (fresh) {
            this.selectedRun = fresh;
            this.maybeClearApprovalSubmitting(fresh);
          }
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
    this.loadDebates(run.runId);
    this.cdr.detectChanges();
  }

  /**
   * Debate turns to render in the Strategy panel for the selected run. Prefers
   * the dedicated DebateRecord documents fetched from `/api/debates/{runId}`
   * (using the most recent refinement iteration); falls back to the transcript
   * persisted on the GraphRun for older runs that predate the DebateRecord
   * feature.
   */
  get debateTurns(): DebateTurnSnapshot[] {
    if (this.debateRecords.length > 0) {
      // Records arrive ordered by refinementCount ascending (see
      // RavenDebateRepository), so the last element is the most recent
      // refinement iteration's debate.
      return this.debateRecords[this.debateRecords.length - 1].turns;
    }
    return this.selectedRun?.strategyDebate ?? [];
  }

  /**
   * Fetches persisted debate transcripts for a run. Best-effort — on error the
   * panel simply falls back to the transcript on the GraphRun snapshot.
   */
  private loadDebates(runId: string): void {
    this.debateRecords = [];
    this.api.getDebates(runId).subscribe({
      next: records => {
        // Guard against a stale response arriving after the user selected a
        // different run.
        if (this.selectedRun?.runId === runId) {
          this.debateRecords = records;
          this.cdr.detectChanges();
        }
      },
      error: () => { /* fall back to GraphRun.strategyDebate */ },
    });
  }

  /** Expand the active node and default it to the reasoning tab. */
  private autoExpandActive(run: GraphRun): void {
    run.nodes?.forEach(n => {
      if (n.status === 'Active') {
        this.expandedNodes.add(n.nodeId);
        if (!this.activeTab.has(n.nodeId)) {
          // approval-node never streams reasoning — the active state IS the
          // approval CTA, so open the result tab so the Approve/Reject
          // buttons are visible without an extra click.
          this.activeTab.set(n.nodeId, n.nodeId === 'approval-node' ? 'result' : 'reasoning');
        }
      } else if (n.status === 'Completed' && this.activeTab.get(n.nodeId) === 'reasoning') {
        // Once the node has a result, flip back to the result tab
        this.activeTab.set(n.nodeId, 'result');
      }
    });
  }

  private appendStream(chunk: NodeStreamChunk): void {
    const key = `${chunk.runId}:${chunk.nodeId}`;
    const isFirstChunk = !this.nodeStreams.has(key);
    const current = this.nodeStreams.get(key) ?? '';
    this.nodeStreams.set(key, current + chunk.text);
    if (isFirstChunk && chunk.nodeId !== 'approval-node') {
      // Reasoning just started streaming for this node — always surface the
      // reasoning tab so the tokens are visible as they arrive. We override
      // any prior tab choice here so a node re-entered on a refinement loop
      // (same nodeId, fresh reasoning) also flips back to reasoning. The
      // `Completed → result` flip in autoExpandActive will move it back
      // when the node finishes. approval-node is excluded — it never streams,
      // and its result tab carries the approve/reject CTAs.
      this.activeTab.set(chunk.nodeId, 'reasoning');
    }
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
    // approval-node has no reasoning stream — keep it on the result tab
    // (which renders the approve/reject CTAs) so the user isn't shown an
    // empty "Waiting for the model to start streaming…" placeholder.
    if (node.nodeId === 'approval-node') return 'result';
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

  /**
   * Initial trailing-stop trigger price = currentPrice * (1 - trailPercent/100).
   * Returns null when either input is missing so the template can hide the line.
   * Note: the live broker stop ratchets up with price; this is the starting level.
   */
  trailingStopPrice(s: StrategyOutput): number | null {
    if (s.currentPrice == null || s.trailPercent == null) return null;
    const stop = s.currentPrice * (1 - s.trailPercent / 100);
    return stop > 0 ? stop : null;
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

  /**
   * Order value to display on the risk card. Prefers the strategy's
   * pre-computed `estimatedCost` for the original quantity, but recalculates
   * from `currentPrice` × `adjustedQuantity` when the Risk hound modified the
   * size. Returns `null` when there's no price to anchor on.
   */
  riskOrderValue(r: RiskOutput): number | null {
    const price = r.decision?.currentPrice;
    if (r.adjustedQuantity != null && price != null) {
      return r.adjustedQuantity * price;
    }
    if (r.decision?.estimatedCost != null) {
      return r.decision.estimatedCost;
    }
    if (price != null && r.decision?.quantity != null) {
      return r.decision.quantity * price;
    }
    return null;
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

  /**
   * Background/border styling for the coordinator-verdict banner that closes
   * the strategy-debate panel. Mirrors `actionClass` colours but uses a
   * stronger border and softer fill so the banner reads as a conclusion to
   * the bull-vs-bear transcript above it.
   */
  verdictBannerClass(action?: string): string {
    switch (action?.toLowerCase()) {
      case 'buy': return 'bg-green-900/20 border-green-600 text-green-100';
      case 'sell': return 'bg-red-900/20 border-red-600 text-red-100';
      default: return 'bg-yellow-900/20 border-yellow-600 text-yellow-100';
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
    if (node.status === 'Skipped') return 'Skipped';
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
    // Paint the disabled state immediately so a slow round-trip can't be
    // mistaken for a missed click — the buttons stay disabled until the
    // worker resumes the graph and SignalR pushes the new approvalStatus.
    this.cdr.detectChanges();

    const runId = this.selectedRun.runId;
    const notes = this.approvalNotes.trim() || undefined;
    const call = decision === 'approve'
      ? this.api.approveRun(runId, 'dashboard-user', notes)
      : this.api.rejectRun(runId, 'dashboard-user', notes);

    call.subscribe({
      next: () => {
        // Do NOT clear approvalSubmitting here. The API only writes the
        // GraphApproval document; the trading worker polls and applies it
        // asynchronously. Keeping the buttons disabled until SignalR pushes
        // the resumed run state prevents double-submits in that gap and
        // gives the user accurate “still working” feedback.
        this.approvalNotes = '';
        // Nudge a refresh so the new state arrives quickly even if the
        // SignalR push is delayed.
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

  /**
   * Clears the in-flight approval flag once the worker has applied the
   * decision and pushed the new run state. We watch for the
   * <c>Pending → Approved/Rejected</c> transition on the currently selected
   * run so the Approve/Reject buttons re-enable (or stay hidden) at exactly
   * the moment the graph has actually resumed.
   */
  private maybeClearApprovalSubmitting(run: GraphRun): void {
    if (!this.approvalSubmitting) return;
    if (this.selectedRun?.runId !== run.runId) return;
    if (run.approvalStatus && run.approvalStatus !== 'Pending') {
      this.approvalSubmitting = null;
    }
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

  private mergeRun(run: GraphRun): boolean {
    const idx = this.runs.findIndex(r => r.runId === run.runId);
    if (idx >= 0) {
      this.runs[idx] = run;
      return false;
    }
    this.runs.unshift(run);
    return true;
  }
}
