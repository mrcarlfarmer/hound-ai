import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { Pack, HealthReport, ServiceHealth } from '../../models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard.component.html',
  styles: []
})
export class DashboardComponent implements OnInit, OnDestroy {
  packs: Pack[] = [];
  loading = false;
  healthReport?: HealthReport;
  healthError = false;
  private refreshInterval?: ReturnType<typeof setInterval>;

  constructor(private api: ApiService, private cdr: ChangeDetectorRef) {}

  ngOnInit(): void {
    this.loading = true;
    this.api.getPacks().subscribe({
      next: packs => {
        this.packs = packs;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.cdr.detectChanges();
      },
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
        this.cdr.detectChanges();
      },
      error: () => {
        this.healthError = true;
        this.cdr.detectChanges();
      }
    });
  }
}
