import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { GraphRunsComponent } from './graph-runs.component';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { GraphRun } from '../../models';

describe('GraphRunsComponent', () => {
  const mockRun: GraphRun = {
    id: 'GraphRuns/AAPL-20260510-abc12345',
    runId: 'AAPL-20260510-abc12345',
    symbol: 'AAPL',
    startedAt: '2026-05-10T12:00:00Z',
    phase: 'Entry',
    isComplete: true,
    refinementCount: 0,
    monitorCycleCount: 0,
    nodes: [
      { nodeId: 'analysts-team-node', status: 'Completed', outputJson: '{"symbol":"AAPL","lastPrice":190.5,"volumeChange":1.2,"trend":"Bullish","confidenceScore":0.85,"summary":"Strong uptrend","marketReport":"Price is trending up","fundamentalsReport":"Solid fundamentals","newsReport":"Positive earnings","sentimentReport":"Bullish sentiment"}' },
      { nodeId: 'strategy-node', status: 'Completed', outputJson: '{"symbol":"AAPL","action":"Buy","quantity":10,"reasoning":"Momentum play","confidence":0.8}' },
      { nodeId: 'risk-node', status: 'Completed', outputJson: '{"verdict":"Approved","reasoning":"Within limits"}' },
      { nodeId: 'execution-node', status: 'Completed', outputJson: '{"success":true,"symbol":"AAPL","orderId":"order-1","message":"Filled"}' },
      { nodeId: 'monitor-node', status: 'Pending' },
    ],
  };

  function createComponent(runs: GraphRun[] = []) {
    const mockApi = {
      getRuns: vi.fn().mockReturnValue(of(runs)),
      getRun: vi.fn().mockReturnValue(of(runs[0])),
    };
    const mockSignalr = {
      connect: vi.fn(),
      disconnect: vi.fn(),
      subscribeToPack: vi.fn(),
      unsubscribeFromPack: vi.fn(),
      onGraphRunUpdate$: of(),
    };

    TestBed.configureTestingModule({
      imports: [GraphRunsComponent],
      providers: [
        { provide: ApiService, useValue: mockApi },
        { provide: SignalrService, useValue: mockSignalr },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(GraphRunsComponent);
    fixture.detectChanges();
    return { fixture, mockApi, mockSignalr };
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('should create', () => {
    const { fixture } = createComponent();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should show empty state when no runs', () => {
    const { fixture } = createComponent([]);
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('No runs yet');
  });

  it('should render run in sidebar', () => {
    const { fixture } = createComponent([mockRun]);
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('AAPL');
    expect(text).toContain('Complete');
  });

  it('should auto-select first run', () => {
    const { fixture } = createComponent([mockRun]);
    expect(fixture.componentInstance.selectedRun?.runId).toBe(mockRun.runId);
  });

  it('should render node pipeline for selected run', () => {
    const { fixture } = createComponent([mockRun]);
    const nodes = fixture.nativeElement.querySelectorAll('button[class*="rounded-lg"]');
    // 5 nodes in the pipeline
    expect(nodes.length).toBeGreaterThanOrEqual(5);
  });

  it('should expand completed nodes by default', () => {
    const { fixture } = createComponent([mockRun]);
    const component = fixture.componentInstance;
    expect(component.isExpanded('analysts-team-node')).toBe(true);
    expect(component.isExpanded('monitor-node')).toBe(false);
  });

  it('should toggle node expansion', () => {
    const { fixture } = createComponent([mockRun]);
    const component = fixture.componentInstance;
    component.toggleNode('analysts-team-node');
    expect(component.isExpanded('analysts-team-node')).toBe(false);
    component.toggleNode('analysts-team-node');
    expect(component.isExpanded('analysts-team-node')).toBe(true);
  });

  it('should parse node output JSON', () => {
    const { fixture } = createComponent([mockRun]);
    const component = fixture.componentInstance;
    const output = component.parseOutput('{"key":"value"}');
    expect(output).toEqual({ key: 'value' });
  });

  it('should return null for invalid JSON', () => {
    const { fixture } = createComponent([mockRun]);
    const component = fixture.componentInstance;
    expect(component.parseOutput('not json')).toBeNull();
    expect(component.parseOutput(undefined)).toBeNull();
  });

  it('should subscribe to SignalR on init', () => {
    const { mockSignalr } = createComponent([mockRun]);
    expect(mockSignalr.connect).toHaveBeenCalled();
    expect(mockSignalr.subscribeToPack).toHaveBeenCalledWith('trading-pack');
  });

  it('should parse analysts output', () => {
    const { fixture } = createComponent([mockRun]);
    const c = fixture.componentInstance;
    const a = c.parseAnalystsOutput(mockRun.nodes[0].outputJson);
    expect(a?.symbol).toBe('AAPL');
    expect(a?.lastPrice).toBe(190.5);
    expect(a?.marketReport).toBe('Price is trending up');
  });

  it('should return correct trend classes', () => {
    const { fixture } = createComponent();
    const c = fixture.componentInstance;
    expect(c.trendClass('Bullish')).toContain('green');
    expect(c.trendClass('Bearish')).toContain('red');
    expect(c.trendClass('Neutral')).toContain('yellow');
  });

  it('should compute confidence width and color', () => {
    const { fixture } = createComponent();
    const c = fixture.componentInstance;
    expect(c.confidenceWidth(0.85)).toBe('85%');
    expect(c.confidenceColor(0.85)).toContain('green');
    expect(c.confidenceColor(0.5)).toContain('yellow');
    expect(c.confidenceColor(0.2)).toContain('red');
  });

  it('should normalize confidence values > 1 as percentages', () => {
    const { fixture } = createComponent();
    const c = fixture.componentInstance;
    expect(c.confidenceWidth(72)).toBe('72%');
    expect(c.confidencePercent(4.0)).toBe('40%');
    expect(c.confidencePercent(7.2)).toBe('72%');
    expect(c.confidencePercent(72)).toBe('72%');
    expect(c.confidencePercent(0.85)).toBe('85%');
    expect(c.confidencePercent(undefined)).toBe('—');
  });

  it('should detect analyst reports', () => {
    const { fixture } = createComponent();
    const c = fixture.componentInstance;
    expect(c.hasAnalystReports({ marketReport: 'report' })).toBe(true);
    expect(c.hasAnalystReports({})).toBe(false);
  });

  it('should render analysts team metrics when expanded', () => {
    const { fixture } = createComponent([mockRun]);
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('$190.50');
    expect(text).toContain('Bullish');
    expect(text).toContain('85%');
  });

  it('should parse strategy output', () => {
    const { fixture } = createComponent([mockRun]);
    const c = fixture.componentInstance;
    const s = c.parseStrategyOutput(mockRun.nodes[1].outputJson);
    expect(s?.symbol).toBe('AAPL');
    expect(s?.action).toBe('Buy');
    expect(s?.quantity).toBe(10);
  });

  it('should return correct action classes', () => {
    const { fixture } = createComponent();
    const c = fixture.componentInstance;
    expect(c.actionClass('Buy')).toContain('green');
    expect(c.actionClass('Sell')).toContain('red');
    expect(c.actionClass('Hold')).toContain('yellow');
  });

  it('should render strategy action badge when expanded', () => {
    const { fixture } = createComponent([mockRun]);
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Buy');
    expect(text).toContain('Momentum play');
  });

  it('should show No action for downstream nodes when strategy is Hold', () => {
    const holdRun: GraphRun = {
      ...mockRun,
      nodes: [
        { nodeId: 'analysts-team-node', status: 'Completed', outputJson: '{}' },
        { nodeId: 'strategy-node', status: 'Completed', outputJson: '{"action":"Hold","quantity":0,"reasoning":"No signal","confidence":0.3}' },
        { nodeId: 'risk-node', status: 'Pending' },
        { nodeId: 'execution-node', status: 'Pending' },
        { nodeId: 'monitor-node', status: 'Pending' },
      ],
    };
    const { fixture } = createComponent([holdRun]);
    const c = fixture.componentInstance;
    expect(c.displayStatus(holdRun.nodes[2])).toBe('No action');
    expect(c.displayStatus(holdRun.nodes[3])).toBe('No action');
    expect(c.displayStatus(holdRun.nodes[4])).toBe('No action');
  });

  it('should show Pending for downstream nodes when strategy is Buy', () => {
    const buyRun: GraphRun = {
      ...mockRun,
      nodes: [
        { nodeId: 'analysts-team-node', status: 'Completed', outputJson: '{}' },
        { nodeId: 'strategy-node', status: 'Completed', outputJson: '{"action":"Buy","quantity":10,"reasoning":"Go","confidence":0.9}' },
        { nodeId: 'risk-node', status: 'Pending' },
        { nodeId: 'execution-node', status: 'Pending' },
        { nodeId: 'monitor-node', status: 'Pending' },
      ],
    };
    const { fixture } = createComponent([buyRun]);
    const c = fixture.componentInstance;
    expect(c.displayStatus(buyRun.nodes[2])).toBe('Pending');
    expect(c.displayStatus(buyRun.nodes[3])).toBe('Pending');
  });
});
