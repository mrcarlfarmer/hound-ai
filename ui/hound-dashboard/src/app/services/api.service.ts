import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Pack, HoundInfo, ActivityLog, ActivityFilter, PagedResult, WatchtowerEvent, HealthReport } from '../models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly baseUrl = 'http://localhost:5000';

  constructor(private http: HttpClient) {}

  getPacks(): Observable<Pack[]> {
    return this.http.get<Pack[]>(`${this.baseUrl}/api/packs`);
  }

  getPack(id: string): Observable<Pack> {
    return this.http.get<Pack>(`${this.baseUrl}/api/packs/${id}`);
  }

  getHounds(packId: string): Observable<HoundInfo[]> {
    return this.http.get<HoundInfo[]>(`${this.baseUrl}/api/packs/${packId}/hounds`);
  }

  getActivity(filters: ActivityFilter): Observable<PagedResult<ActivityLog>> {
    let params = new HttpParams();
    if (filters.pack) params = params.set('pack', filters.pack);
    if (filters.hound) params = params.set('hound', filters.hound);
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.page) params = params.set('page', filters.page.toString());
    if (filters.pageSize) params = params.set('pageSize', filters.pageSize.toString());
    return this.http.get<PagedResult<ActivityLog>>(`${this.baseUrl}/api/activity`, { params });
  }

  getWatchtowerEvents(page = 1, pageSize = 50): Observable<WatchtowerEvent[]> {
    const params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<WatchtowerEvent[]>(`${this.baseUrl}/api/watchtower`, { params });
  }

  getHealth(): Observable<HealthReport> {
    return this.http.get<HealthReport>(`${this.baseUrl}/api/health`);
  }
}
