import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { ApiService } from '../../services/api.service';
import { Pack } from '../../models';

describe('DashboardComponent', () => {
  const mockPacks: Pack[] = [
    { id: '1', name: 'Trading Pack', status: 'Running', houndCount: 3 },
    { id: '2', name: 'Test Pack', status: 'Idle', houndCount: 1 },
  ];

  function createComponent(packs: Pack[] = []) {
    const mockApi = { getPacks: vi.fn().mockReturnValue(of(packs)) };
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
        { provide: ApiService, useValue: { getPacks: vi.fn().mockReturnValue(of([])) } },
      ],
    }).compileComponents();
  });

  it('should create', () => {
    const { fixture } = createComponent();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should call ApiService.getPacks on init', () => {
    const { mockApi } = createComponent();
    expect(mockApi.getPacks).toHaveBeenCalled();
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
});
