import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  provideHttpClientTesting,
  HttpTestingController,
} from '@angular/common/http/testing';
import { WatchtowerComponent } from './watchtower.component';
import { WatchtowerEvent } from '../../models';

describe('WatchtowerComponent', () => {
  let httpMock: HttpTestingController;

  const WATCHTOWER_URL = 'http://localhost:5000/api/watchtower';

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WatchtowerComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(WatchtowerComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance).toBeTruthy();
    httpMock.expectOne(r => r.url === WATCHTOWER_URL).flush([]);
  });

  it('should render a table row for each event', async () => {
    const events: WatchtowerEvent[] = [
      {
        id: '1',
        containerName: 'trading-pack',
        imageName: 'ghcr.io/hound-ai/trading-pack:latest',
        oldImageId: 'abc1234',
        newImageId: 'def5678',
        action: 'Updated',
        timestamp: '2026-04-12T10:00:00Z',
        rawPayload: '{}',
      },
      {
        id: '2',
        containerName: 'hound-api',
        imageName: 'ghcr.io/hound-ai/hound-api:latest',
        oldImageId: 'ghi9012',
        newImageId: 'jkl3456',
        action: 'Updated',
        timestamp: '2026-04-12T10:05:00Z',
        rawPayload: '{}',
      },
    ];

    const fixture = TestBed.createComponent(WatchtowerComponent);
    fixture.autoDetectChanges();
    httpMock.expectOne(r => r.url === WATCHTOWER_URL).flush(events);
    await fixture.whenStable();

    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('trading-pack');
    expect(rows[1].textContent).toContain('hound-api');
  });

  it('should show empty message when no events are returned', async () => {
    const fixture = TestBed.createComponent(WatchtowerComponent);
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === WATCHTOWER_URL).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const empty = fixture.nativeElement.querySelector('.empty');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('No watchtower events');
  });

  it('should display image IDs in mono font cells', async () => {
    const events: WatchtowerEvent[] = [
      {
        id: '1',
        containerName: 'test',
        imageName: 'img:latest',
        oldImageId: 'old123',
        newImageId: 'new456',
        action: 'Updated',
        timestamp: '2026-04-12T10:00:00Z',
        rawPayload: '{}',
      },
    ];

    const fixture = TestBed.createComponent(WatchtowerComponent);
    fixture.autoDetectChanges();
    httpMock.expectOne(r => r.url === WATCHTOWER_URL).flush(events);
    await fixture.whenStable();

    const monoCells = fixture.nativeElement.querySelectorAll('.mono');
    expect(monoCells.length).toBe(2);
    expect(monoCells[0].textContent).toContain('old123');
    expect(monoCells[1].textContent).toContain('new456');
  });
});
