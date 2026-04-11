import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  provideHttpClientTesting,
  HttpTestingController,
} from '@angular/common/http/testing';
import { ActivityLogComponent } from './activity-log.component';
import { ActivityLog } from '../../models';

describe('ActivityLogComponent', () => {
  let httpMock: HttpTestingController;

  const ACTIVITY_URL = 'http://localhost:5000/api/activity';

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActivityLogComponent],
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
    const fixture = TestBed.createComponent(ActivityLogComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance).toBeTruthy();
    httpMock.expectOne(r => r.url === ACTIVITY_URL).flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('should render a table row for each activity item', async () => {
    const activities: ActivityLog[] = [
      {
        id: '1',
        packId: 'p1',
        houndId: 'h1',
        houndName: 'AnalysisHound',
        message: 'Analysis complete',
        severity: 'Info',
        timestamp: '2024-01-15T10:30:00Z',
      },
      {
        id: '2',
        packId: 'p1',
        houndId: 'h2',
        houndName: 'RiskHound',
        message: 'Risk limit breached',
        severity: 'Warning',
        timestamp: '2024-01-15T10:31:00Z',
      },
    ];

    const fixture = TestBed.createComponent(ActivityLogComponent);
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === ACTIVITY_URL).flush({
      items: activities,
      totalCount: 2,
      page: 1,
      pageSize: 20,
    });
    await fixture.whenStable();
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Analysis complete');
    expect(rows[1].textContent).toContain('Risk limit breached');
  });

  it('should show severity badge in each row', async () => {
    const activities: ActivityLog[] = [
      {
        id: '1',
        packId: 'p1',
        houndId: 'h1',
        houndName: 'AnalysisHound',
        message: 'Error occurred',
        severity: 'Error',
        timestamp: '2024-01-15T10:30:00Z',
      },
    ];

    const fixture = TestBed.createComponent(ActivityLogComponent);
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === ACTIVITY_URL).flush({
      items: activities,
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    await fixture.whenStable();
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.badge');
    expect(badge?.textContent?.trim()).toBe('Error');
    expect(badge?.classList).toContain('error');
  });

  it('should show empty message when no activity is returned', async () => {
    const fixture = TestBed.createComponent(ActivityLogComponent);
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === ACTIVITY_URL).flush({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
    });
    await fixture.whenStable();
    fixture.detectChanges();

    const empty = fixture.nativeElement.querySelector('.empty');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('No activity found');
  });

  it('should reload activities when Search button is clicked', async () => {
    const fixture = TestBed.createComponent(ActivityLogComponent);
    fixture.detectChanges();
    // initial load
    httpMock.expectOne(r => r.url === ACTIVITY_URL).flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    await fixture.whenStable();
    fixture.detectChanges();

    // click search
    const button = fixture.nativeElement.querySelector('button');
    button.click();
    fixture.detectChanges();

    // second request
    const req = httpMock.expectOne(r => r.url === ACTIVITY_URL);
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });
});
