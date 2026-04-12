import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  provideHttpClientTesting,
  HttpTestingController,
} from '@angular/common/http/testing';
import { TunerService } from './tuner.service';
import { TunerExperiment, PagedResult } from '../models';

describe('TunerService', () => {
  let service: TunerService;
  let httpMock: HttpTestingController;

  const BASE = 'http://localhost:5000/api/tuner/experiments';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TunerService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('getExperiments() should GET /api/tuner/experiments with page and pageSize', () => {
    const mockResult: PagedResult<TunerExperiment> = {
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
    };
    service.getExperiments(1, 20).subscribe(result => {
      expect(result).toEqual(mockResult);
    });
    const req = httpMock.expectOne(r => r.url === BASE);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush(mockResult);
  });

  it('applyExperiment() should POST /api/tuner/experiments/{id}/apply', () => {
    service.applyExperiment('exp-1').subscribe();
    const req = httpMock.expectOne(`${BASE}/exp-1/apply`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('rejectExperiment() should POST /api/tuner/experiments/{id}/reject', () => {
    service.rejectExperiment('exp-2').subscribe();
    const req = httpMock.expectOne(`${BASE}/exp-2/reject`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });
});
