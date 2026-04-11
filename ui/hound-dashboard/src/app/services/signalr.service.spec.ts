import { TestBed } from '@angular/core/testing';
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

  beforeEach(() => {
    vi.clearAllMocks();
    mockConnection.start.mockResolvedValue(undefined);

    TestBed.configureTestingModule({
      providers: [SignalrService],
    });
    service = TestBed.inject(SignalrService);
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

  it('onActivity$ should emit when the hub fires OnActivity', () => {
    const received: ActivityLog[] = [];
    service.onActivity$.subscribe(a => received.push(a));

    service.connect();

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
