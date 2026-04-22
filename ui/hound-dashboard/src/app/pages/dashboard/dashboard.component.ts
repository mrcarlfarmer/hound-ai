import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { Pack, HealthReport, ServiceHealth } from '../../models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <h1 class="mb-6 font-heading text-3xl font-semibold tracking-tight text-foreground">Hound AI Dashboard</h1>

    <!-- Service Health Strip -->
    <div class="mb-6 rounded-lg border border-border bg-card p-4">
      <div class="mb-2 flex items-center gap-2">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Service Health</h2>
        <span *ngIf="healthReport" class="h-2 w-2 rounded-full"
              [ngClass]="{
                'bg-green-400': healthReport.status === 'Healthy',
                'bg-yellow-400': healthReport.status === 'Degraded',
                'bg-red-400': healthReport.status === 'Unhealthy',
                'bg-secondary': healthReport.status === 'Unknown'
              }"></span>
      </div>
      <div *ngIf="!healthReport" class="flex gap-4">
        <div *ngFor="let _ of [1,2,3,4]" class="h-10 w-36 animate-pulse rounded-md bg-secondary/50"></div>
      </div>
      <div *ngIf="healthReport" class="flex flex-wrap gap-3">
        <div *ngFor="let svc of healthReport.services"
             class="flex items-center gap-2 rounded-md border border-border px-3 py-2">
          <span class="h-2.5 w-2.5 rounded-full"
                [ngClass]="{
                  'bg-green-400': svc.status === 'Healthy',
                  'bg-yellow-400': svc.status === 'Degraded',
                  'bg-red-400': svc.status === 'Unhealthy',
                  'bg-secondary': svc.status === 'Unknown'
                }"></span>
          <span class="text-sm font-medium text-foreground">{{ svc.name }}</span>
          <span *ngIf="svc.detail" class="text-xs text-muted-foreground">{{ svc.detail }}</span>
        </div>
      </div>
      <p *ngIf="healthError" class="mt-2 text-sm text-red-400">Unable to reach API</p>
    </div>

    <!-- Loading skeleton -->
    <div *ngIf="loading" class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <div *ngFor="let _ of [1,2,3]" class="h-28 animate-pulse rounded-lg bg-secondary/50"></div>
    </div>

    <div *ngIf="!loading" class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-[repeat(auto-fill,minmax(250px,1fr))]">
      <div *ngFor="let pack of packs"
           class="pack-card cursor-pointer rounded-lg border border-border bg-card p-4 transition-colors hover:border-primary"
           [routerLink]="['/packs', pack.id]">
        <h3 class="mb-2 text-lg font-semibold text-card-foreground">{{ pack.name }}</h3>
        <span class="status inline-block rounded-md px-2 py-0.5 text-sm font-medium"
              [ngClass]="{
                'bg-green-900/40 text-green-400': pack.status.toLowerCase() === 'running',
                'bg-secondary text-secondary-foreground': pack.status.toLowerCase() === 'idle' || pack.status.toLowerCase() === 'stopped',
                'bg-red-900/40 text-red-400': pack.status.toLowerCase() === 'error'
              }">{{ pack.status }}</span>
        <p class="mt-2 text-sm text-muted-foreground">{{ pack.houndCount }} hounds</p>
        <p *ngIf="pack.lastActivity" class="mt-1 text-xs text-muted-foreground">Last: {{ pack.lastActivity | date:'short' }}</p>
      </div>
    </div>
    <p *ngIf="!loading && packs.length === 0" class="empty mt-4 italic text-muted-foreground">No packs registered.</p>
  `,
  styles: []
})
export class DashboardComponent implements OnInit, OnDestroy {
  packs: Pack[] = [];
  loading = false;
  healthReport?: HealthReport;
  healthError = false;
  private refreshInterval?: ReturnType<typeof setInterval>;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loading = true;
    this.api.getPacks().subscribe(packs => {
      this.packs = packs;
      this.loading = false;
    });
    this.loadHealth();
    this.refreshInterval = setInterval(() => this.loadHealth(), 30000);
  }

  ngOnDestroy(): void {
    if (this.refreshInterval) clearInterval(this.refreshInterval);
  }

  private loadHealth(): void {
    this.api.getHealth().subscribe({
      next: report => {
        this.healthReport = report;
        this.healthError = false;
      },
      error: () => {
        this.healthError = true;
      }
    });
  }
}
