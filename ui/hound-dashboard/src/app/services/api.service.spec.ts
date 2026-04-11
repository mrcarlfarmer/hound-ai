import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ApiService } from './api.service';
import { provideHttpClient } from '@angular/common/http';
import {
  provideHttpClientTesting,
  HttpTestingController,
} from '@angular/common/http/testing';
import { ApiService } from './api.service';
import { ActivityFilter, Pack } from '../models';

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
      providers: [provideHttpClient(), provideHttpClientTesting()],
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
    const mockPacks: Pack[] = [
      { id: '1', name: 'Trading Pack', status: 'Running', houndCount: 3 },
    ];
    service.getPacks().subscribe(packs => {
      expect(packs).toEqual(mockPacks);
    });
    const req = httpMock.expectOne('http://localhost:5000/api/packs');
    expect(req.request.method).toBe('GET');
    req.flush(mockPacks);
  });

  it('getPack(id) should GET /api/packs/{id}', () => {
    const mockPack: Pack = { id: 'pack1', name: 'Trading Pack', status: 'Idle', houndCount: 2 };
    service.getPack('pack1').subscribe(pack => {
      expect(pack).toEqual(mockPack);
    });
    const req = httpMock.expectOne('http://localhost:5000/api/packs/pack1');
    expect(req.request.method).toBe('GET');
    req.flush(mockPack);
  });

  it('getHounds(packId) should GET /api/packs/{packId}/hounds', () => {
    service.getHounds('pack1').subscribe();
    const req = httpMock.expectOne('http://localhost:5000/api/packs/pack1/hounds');
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
  it('getActivity() should GET /api/activity without params when filter is empty', () => {
    service.getActivity({}).subscribe();
    const req = httpMock.expectOne(r => r.url === 'http://localhost:5000/api/activity');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().length).toBe(0);
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('getActivity() should pass filter params in query string', () => {
    const filter: ActivityFilter = { pack: 'p1', hound: 'h1', page: 2, pageSize: 10 };
    service.getActivity(filter).subscribe();
    const req = httpMock.expectOne(r => r.url === 'http://localhost:5000/api/activity');
    expect(req.request.params.get('pack')).toBe('p1');
    expect(req.request.params.get('hound')).toBe('h1');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('10');
    req.flush({ items: [], totalCount: 0, page: 2, pageSize: 10 });
  });

  it('getActivity() should include date range params when provided', () => {
    const filter: ActivityFilter = { from: '2024-01-01', to: '2024-01-31' };
    service.getActivity(filter).subscribe();
    const req = httpMock.expectOne(r => r.url === 'http://localhost:5000/api/activity');
    expect(req.request.params.get('from')).toBe('2024-01-01');
    expect(req.request.params.get('to')).toBe('2024-01-31');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });
});
