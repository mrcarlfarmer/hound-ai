import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { ApiService } from '../../services/api.service';
import { Pack, HealthReport } from '../../models';

describe('DashboardComponent', () => {
  const mockPacks: Pack[] = [
    { id: '1', name: 'Trading Pack', status: 'Running', houndCount: 3 },
    { id: '2', name: 'Test Pack', status: 'Idle', houndCount: 1 },
  ];

  const mockHealth: HealthReport = {
    status: 'Healthy',
    timestamp: new Date().toISOString(),
    services: [
      { name: 'hound-api', status: 'Healthy' },
      { name: 'ravendb', status: 'Healthy' },
      { name: 'ollama', status: 'Healthy', detail: '2 models loaded' },
      { name: 'trading-pack', status: 'Degraded', detail: 'Last activity 8m ago' },
    ],
  };

  function createComponent(packs: Pack[] = [], health: HealthReport = mockHealth) {
    const mockApi = {
      getPacks: vi.fn().mockReturnValue(of(packs)),
      getHealth: vi.fn().mockReturnValue(of(health)),
    };
    TestBed.overrideProvider(ApiService, { useValue: mockApi });
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    return { fixture, mockApi };
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideRouter([]),
        {
          provide: ApiService,
          useValue: {
            getPacks: vi.fn().mockReturnValue(of([])),
            getHealth: vi.fn().mockReturnValue(of(mockHealth)),
          },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should create', () => {
    const { fixture } = createComponent();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should call ApiService.getPacks on init', () => {
    const { mockApi } = createComponent();
    expect(mockApi.getPacks).toHaveBeenCalled();
  });

  it('should call ApiService.getHealth on init', () => {
    const { mockApi } = createComponent();
    expect(mockApi.getHealth).toHaveBeenCalled();
  });

  it('should render a card for each pack returned by the API', () => {
    const { fixture } = createComponent(mockPacks);

    const cards = fixture.nativeElement.querySelectorAll('.pack-card');
    expect(cards.length).toBe(2);
    expect(cards[0].querySelector('h3')?.textContent?.trim()).toBe('Trading Pack');
    expect(cards[1].querySelector('h3')?.textContent?.trim()).toBe('Test Pack');
  });

  it('should show the status badge on each card', () => {
    const { fixture } = createComponent([mockPacks[0]]);

    const badge = fixture.nativeElement.querySelector('.status');
    expect(badge?.textContent?.trim()).toBe('Running');
  });

  it('should show an empty message when no packs are returned', () => {
    const { fixture } = createComponent([]);

    const empty = fixture.nativeElement.querySelector('.empty');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('No packs registered');
  });

  it('should render service health indicators', () => {
    const { fixture } = createComponent([], mockHealth);

    const serviceNames = fixture.nativeElement.querySelectorAll('.font-medium');
    const names = Array.from(serviceNames).map((el: any) => el.textContent?.trim());
    expect(names).toContain('hound-api');
    expect(names).toContain('ravendb');
    expect(names).toContain('ollama');
    expect(names).toContain('trading-pack');
  });

  it('should show health error message when API is unreachable', () => {
    const mockApi = {
      getPacks: vi.fn().mockReturnValue(of([])),
      getHealth: vi.fn().mockReturnValue(throwError(() => new Error('Network error'))),
    };
    TestBed.overrideProvider(ApiService, { useValue: mockApi });
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.healthError).toBe(true);
  });
});
