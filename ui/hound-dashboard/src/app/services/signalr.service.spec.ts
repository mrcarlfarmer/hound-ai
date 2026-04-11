import { TestBed } from '@angular/core/testing';
import { SignalrService } from './signalr.service';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { SignalrService } from './signalr.service';
import { ActivityLog } from '../models';

const mockConnection = vi.hoisted(() => ({
  on: vi.fn(),
  start: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn(),
  invoke: vi.fn(),
}));

vi.mock('@microsoft/signalr', () => ({
  // vi.fn() with an arrow function cannot be called with `new`.
  // Using a regular function allows `new signalR.HubConnectionBuilder()` to work in production code.
  HubConnectionBuilder: vi.fn(function () {
    return {
      withUrl: vi.fn().mockReturnThis(),
      withAutomaticReconnect: vi.fn().mockReturnThis(),
      build: vi.fn().mockReturnValue(mockConnection),
    };
  }),
}));

describe('SignalrService', () => {
  let service: SignalrService;

  const mockHubConnection = {
    on: vi.fn(),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    invoke: vi.fn().mockResolvedValue(undefined),
  };

  const mockHubConnectionBuilder = {
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    build: vi.fn().mockReturnValue(mockHubConnection),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    // HubConnectionBuilder is a class, so the mock implementation must be a regular function
    vi.spyOn(signalR, 'HubConnectionBuilder' as any).mockImplementation(
      function (this: any) { return mockHubConnectionBuilder; }
    );
  beforeEach(() => {
    vi.clearAllMocks();
    mockConnection.start.mockResolvedValue(undefined);

    TestBed.configureTestingModule({
      providers: [SignalrService],
    });
    service = TestBed.inject(SignalrService);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('onActivity$ should be an Observable', () => {
  it('should expose onActivity$ as an Observable', () => {
    expect(service.onActivity$).toBeDefined();
    expect(typeof service.onActivity$.subscribe).toBe('function');
  });

  it('connect() should build and start a HubConnection', () => {
    service.connect();
    expect(mockHubConnectionBuilder.withUrl).toHaveBeenCalledWith('http://localhost:5000/hubs/activity');
    expect(mockHubConnectionBuilder.withAutomaticReconnect).toHaveBeenCalled();
    expect(mockHubConnectionBuilder.build).toHaveBeenCalled();
    expect(mockHubConnection.start).toHaveBeenCalled();
  });

  it('connect() should register OnActivity handler', () => {
    service.connect();
    expect(mockHubConnection.on).toHaveBeenCalledWith('OnActivity', expect.any(Function));
  });

  it('disconnect() should stop the hub connection', () => {
    service.connect();
    service.disconnect();
    expect(mockHubConnection.stop).toHaveBeenCalled();
  });

  it('disconnect() should not throw when not connected', () => {
    expect(() => service.disconnect()).not.toThrow();
  });

  it('subscribeToPack() should invoke SubscribeToPack on the hub', () => {
    service.connect();
    service.subscribeToPack('trading-pack');
    expect(mockHubConnection.invoke).toHaveBeenCalledWith('SubscribeToPack', 'trading-pack');
  });

  it('unsubscribeFromPack() should invoke UnsubscribeFromPack on the hub', () => {
    service.connect();
    service.unsubscribeFromPack('trading-pack');
    expect(mockHubConnection.invoke).toHaveBeenCalledWith('UnsubscribeFromPack', 'trading-pack');
  });

  it('onActivity$ should emit when OnActivity handler is called', () => {
    const received: any[] = [];
  it('connect() should register the OnActivity handler and start the connection', () => {
    service.connect();
    expect(mockConnection.on).toHaveBeenCalledWith('OnActivity', expect.any(Function));
    expect(mockConnection.start).toHaveBeenCalled();
  });

  it('subscribeToPack() should invoke SubscribeToPack on the hub', () => {
    service.connect();
    service.subscribeToPack('pack1');
    expect(mockConnection.invoke).toHaveBeenCalledWith('SubscribeToPack', 'pack1');
  });

  it('unsubscribeFromPack() should invoke UnsubscribeFromPack on the hub', () => {
    service.connect();
    service.unsubscribeFromPack('pack1');
    expect(mockConnection.invoke).toHaveBeenCalledWith('UnsubscribeFromPack', 'pack1');
  });

  it('disconnect() should stop the hub connection', () => {
    service.connect();
    service.disconnect();
    expect(mockConnection.stop).toHaveBeenCalled();
  });

  it('onActivity$ should emit when the hub fires OnActivity', () => {
    const received: ActivityLog[] = [];
    service.onActivity$.subscribe(a => received.push(a));

    service.connect();

    const onActivityCall = mockHubConnection.on.mock.calls.find((c: any[]) => c[0] === 'OnActivity');
    expect(onActivityCall).toBeDefined();

    const handler = onActivityCall![1];
    const activity = {
      id: 'log-1',
      packId: 'trading-pack',
      houndId: 'analysis-hound',
      houndName: 'AnalysisHound',
      message: 'Test',
      severity: 'Info',
      timestamp: new Date().toISOString(),
    };
    handler(activity);

    expect(received).toHaveLength(1);
    expect(received[0]).toEqual(activity);
    const mockActivity: ActivityLog = {
      id: '1',
      packId: 'p1',
      houndId: 'h1',
      houndName: 'AnalysisHound',
      message: 'Analysis complete',
      severity: 'Info',
      timestamp: '2024-01-01T00:00:00Z',
    };

    // Retrieve the handler registered via hubConnection.on('OnActivity', handler)
    const onCall = (mockConnection.on as ReturnType<typeof vi.fn>).mock.calls.find(
      (c: unknown[]) => c[0] === 'OnActivity',
    );
    expect(onCall).toBeDefined();
    const handler = onCall![1] as (a: ActivityLog) => void;
    handler(mockActivity);

    expect(received).toHaveLength(1);
    expect(received[0]).toEqual(mockActivity);
  });
});
