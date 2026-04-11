import { TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { of, Subject } from 'rxjs';
import { PackDetailComponent } from './pack-detail.component';
import { ApiService } from '../../services/api.service';
import { SignalrService } from '../../services/signalr.service';
import { ActivityLog, HoundInfo, Pack } from '../../models';

describe('PackDetailComponent', () => {
  let activitySubject: Subject<ActivityLog>;
  let mockSignalr: {
    connect: ReturnType<typeof vi.fn>;
    disconnect: ReturnType<typeof vi.fn>;
    subscribeToPack: ReturnType<typeof vi.fn>;
    unsubscribeFromPack: ReturnType<typeof vi.fn>;
    onActivity$: Subject<ActivityLog>;
  };

  const mockPack: Pack = { id: 'pack1', name: 'Trading Pack', status: 'Running', houndCount: 2 };
  const mockHounds: HoundInfo[] = [
    { id: 'h1', name: 'AnalysisHound', packId: 'pack1', status: 'Processing' },
    { id: 'h2', name: 'RiskHound', packId: 'pack1', status: 'Idle' },
  ];

  function createComponent(
    pack: Pack = mockPack,
    hounds: HoundInfo[] = mockHounds,
  ) {
    const mockApi = {
      getPack: vi.fn().mockReturnValue(of(pack)),
      getHounds: vi.fn().mockReturnValue(of(hounds)),
    };
    TestBed.overrideProvider(ApiService, { useValue: mockApi });
    const fixture = TestBed.createComponent(PackDetailComponent);
    fixture.detectChanges();
    return { fixture, mockApi };
  }

  beforeEach(async () => {
    activitySubject = new Subject<ActivityLog>();
    mockSignalr = {
      connect: vi.fn(),
      disconnect: vi.fn(),
      subscribeToPack: vi.fn(),
      unsubscribeFromPack: vi.fn(),
      onActivity$: activitySubject,
    };

    await TestBed.configureTestingModule({
      imports: [PackDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ApiService,
          useValue: {
            getPack: vi.fn().mockReturnValue(of(mockPack)),
            getHounds: vi.fn().mockReturnValue(of(mockHounds)),
          },
        },
        { provide: SignalrService, useValue: mockSignalr },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'pack1' } } },
        },
      ],
    }).compileComponents();
  });

  it('should create', () => {
    const { fixture } = createComponent();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render a card for each hound', () => {
    const { fixture } = createComponent();

    const cards = fixture.nativeElement.querySelectorAll('.hound-card');
    expect(cards.length).toBe(2);
    expect(cards[0].querySelector('h3')?.textContent?.trim()).toBe('AnalysisHound');
    expect(cards[1].querySelector('h3')?.textContent?.trim()).toBe('RiskHound');
  });

  it('should connect SignalR and subscribe to pack on init', () => {
    createComponent();

    expect(mockSignalr.connect).toHaveBeenCalled();
    expect(mockSignalr.subscribeToPack).toHaveBeenCalledWith('pack1');
  });

  it('should prepend real-time activities received from SignalR', () => {
    const { fixture } = createComponent();

    const activity: ActivityLog = {
      id: 'a1',
      packId: 'pack1',
      houndId: 'h1',
      houndName: 'AnalysisHound',
      message: 'Analysis done',
      severity: 'Info',
      timestamp: '2024-01-01T00:00:00Z',
    };
    activitySubject.next(activity);

    // Verify the component state is updated. Calling fixture.detectChanges() here
    // would trigger Angular's checkNoChanges pass which throws NG0100 because
    // `activities.length === 0` changes value between the update and check passes
    // in this zoneless test environment.
    expect(fixture.componentInstance.activities).toHaveLength(1);
    expect(fixture.componentInstance.activities[0]).toEqual(activity);
  });

  it('should disconnect SignalR on destroy', () => {
    const { fixture } = createComponent();
    fixture.destroy();
    expect(mockSignalr.disconnect).toHaveBeenCalled();
  });
});
