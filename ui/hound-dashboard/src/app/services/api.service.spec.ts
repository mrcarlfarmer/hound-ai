import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ApiService } from './api.service';

describe('ApiService', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ApiService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('getPacks() should GET /api/packs', () => {
    service.getPacks().subscribe();
    const req = httpMock.expectOne('http://localhost:5000/api/packs');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('getPack(id) should GET /api/packs/:id', () => {
    service.getPack('trading-pack').subscribe();
    const req = httpMock.expectOne('http://localhost:5000/api/packs/trading-pack');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('getHounds(packId) should GET /api/packs/:id/hounds', () => {
    service.getHounds('trading-pack').subscribe();
    const req = httpMock.expectOne('http://localhost:5000/api/packs/trading-pack/hounds');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('getActivity() should GET /api/activity with no params when filters are empty', () => {
    service.getActivity({}).subscribe();
    const req = httpMock.expectOne('http://localhost:5000/api/activity');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 50 });
  });

  it('getActivity() should include query params when filters are set', () => {
    service.getActivity({ pack: 'trading-pack', hound: 'analysis-hound', page: 2, pageSize: 25 }).subscribe();
    const req = httpMock.expectOne(r => r.url === 'http://localhost:5000/api/activity');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('pack')).toBe('trading-pack');
    expect(req.request.params.get('hound')).toBe('analysis-hound');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('25');
    req.flush({ items: [], totalCount: 0, page: 2, pageSize: 25 });
  });

  it('getActivity() should include date range params when from/to are set', () => {
    const from = '2024-01-01T00:00:00Z';
    const to = '2024-01-31T23:59:59Z';
    service.getActivity({ from, to }).subscribe();
    const req = httpMock.expectOne(r => r.url === 'http://localhost:5000/api/activity');
    expect(req.request.params.get('from')).toBe(from);
    expect(req.request.params.get('to')).toBe(to);
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 50 });
  });
});
