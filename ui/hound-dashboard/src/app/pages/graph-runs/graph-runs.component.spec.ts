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
      { nodeId: 'data-node', status: 'Completed', outputJson: '{"symbol":"AAPL","lastPrice":190.5,"volumeChange":1.2,"trend":"Bullish","confidenceScore":0.85,"summary":"Strong uptrend"}' },
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
    expect(component.isExpanded('data-node')).toBe(true);
    expect(component.isExpanded('monitor-node')).toBe(false);
  });

  it('should toggle node expansion', () => {
    const { fixture } = createComponent([mockRun]);
    const component = fixture.componentInstance;
    component.toggleNode('data-node');
    expect(component.isExpanded('data-node')).toBe(false);
    component.toggleNode('data-node');
    expect(component.isExpanded('data-node')).toBe(true);
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
});
