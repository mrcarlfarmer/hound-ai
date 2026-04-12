import { TestBed } from '@angular/core/testing';
import { SignalrService, SIGNALR_CONNECTION_FACTORY } from './signalr.service';
import { ActivityLog } from '../models';

describe('SignalrService', () => {
  let service: SignalrService;
  let mockConnection: {
    on: ReturnType<typeof vi.fn>;
    start: ReturnType<typeof vi.fn>;
    stop: ReturnType<typeof vi.fn>;
    invoke: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    mockConnection = {
      on: vi.fn(),
      start: vi.fn().mockResolvedValue(undefined),
      stop: vi.fn(),
      invoke: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        SignalrService,
        { provide: SIGNALR_CONNECTION_FACTORY, useValue: () => mockConnection },
      ],
    });
    service = TestBed.inject(SignalrService);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should expose onActivity$ as an Observable', () => {
    expect(service.onActivity$).toBeDefined();
    expect(typeof service.onActivity$.subscribe).toBe('function');
  });

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

  it('disconnect() should not throw when not connected', () => {
    expect(() => service.disconnect()).not.toThrow();
  });

  it('onActivity$ should emit when the hub fires OnActivity', () => {
    const received: ActivityLog[] = [];
    service.onActivity$.subscribe(a => received.push(a));

    service.connect();

    const onCall = (mockConnection.on as ReturnType<typeof vi.fn>).mock.calls.find(
      (c: unknown[]) => c[0] === 'OnActivity',
    );
    expect(onCall).toBeDefined();
    const handler = onCall![1] as (a: ActivityLog) => void;
    const mockActivity: ActivityLog = {
      id: '1',
      packId: 'p1',
      houndId: 'h1',
      houndName: 'AnalysisHound',
      message: 'Analysis complete',
      severity: 'Info',
      timestamp: '2024-01-01T00:00:00Z',
    };
    handler(mockActivity);

    expect(received).toHaveLength(1);
    expect(received[0]).toEqual(mockActivity);
  });
});
