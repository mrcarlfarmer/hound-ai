import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TunerExperiment, PagedResult } from '../models';

@Injectable({ providedIn: 'root' })
export class TunerService {
  private readonly baseUrl = 'http://localhost:5000';

  constructor(private http: HttpClient) {}

  getExperiments(page: number, pageSize: number): Observable<PagedResult<TunerExperiment>> {
    const params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<PagedResult<TunerExperiment>>(`${this.baseUrl}/api/tuner/experiments`, { params });
  }

  applyExperiment(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/tuner/experiments/${id}/apply`, {});
  }

  rejectExperiment(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/tuner/experiments/${id}/reject`, {});
  }
}
