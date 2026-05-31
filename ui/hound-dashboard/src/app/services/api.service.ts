import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Pack, HoundInfo, ActivityLog, ActivityFilter, PagedResult, HealthReport, TradeDocument, FillStatus, GraphRun, RunRequest, AccountSummary, PositionInfo, AlpacaSyncResult } from '../models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly baseUrl = '';

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

  getHealth(): Observable<HealthReport> {
    return this.http.get<HealthReport>(`${this.baseUrl}/api/health`);
  }

  getTrades(page = 1, pageSize = 20, symbol?: string, fillStatus?: FillStatus): Observable<TradeDocument[]> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    if (symbol) params = params.set('symbol', symbol);
    if (fillStatus) params = params.set('fillStatus', fillStatus);
    return this.http.get<TradeDocument[]>(`${this.baseUrl}/api/trades`, { params });
  }

  getTrade(id: string): Observable<TradeDocument> {
    return this.http.get<TradeDocument>(`${this.baseUrl}/api/trades/${id}`);
  }

  syncTradesFromAlpaca(): Observable<AlpacaSyncResult> {
    return this.http.post<AlpacaSyncResult>(`${this.baseUrl}/api/trades/sync`, {});
  }

  getRuns(limit = 20): Observable<GraphRun[]> {
    const params = new HttpParams().set('limit', limit.toString());
    return this.http.get<GraphRun[]>(`${this.baseUrl}/api/runs`, { params });
  }

  getRun(runId: string): Observable<GraphRun> {
    return this.http.get<GraphRun>(`${this.baseUrl}/api/runs/${runId}`);
  }

  queueRun(symbol: string): Observable<RunRequest> {
    return this.http.post<RunRequest>(`${this.baseUrl}/api/runs`, { symbol });
  }

  getRunRequests(limit = 10): Observable<RunRequest[]> {
    const params = new HttpParams().set('limit', limit.toString());
    return this.http.get<RunRequest[]>(`${this.baseUrl}/api/runs/requests`, { params });
  }

  getAccount(): Observable<AccountSummary> {
    return this.http.get<AccountSummary>(`${this.baseUrl}/api/portfolio/account`);
  }

  getPositions(): Observable<PositionInfo[]> {
    return this.http.get<PositionInfo[]>(`${this.baseUrl}/api/portfolio/positions`);
  }

  closePosition(symbol: string): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/api/portfolio/positions/${encodeURIComponent(symbol)}/close`, {});
  }
}
