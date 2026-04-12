import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TunerService } from '../../services/tuner.service';
import { TunerExperiment } from '../../models';

@Component({
  selector: 'app-tuner',
  standalone: true,
  imports: [CommonModule],
  template: `
    <h1>Tuner Experiments</h1>

    <div class="toolbar">
      <span class="count" *ngIf="totalCount > 0">{{ totalCount }} experiment(s)</span>
    </div>

    <p *ngIf="error" class="error-msg">{{ error }}</p>

    <table class="experiments-table" *ngIf="experiments.length > 0">
      <thead>
        <tr>
          <th>Hound</th>
          <th>Timestamp</th>
          <th>Baseline</th>
          <th>Candidate</th>
          <th>Delta</th>
          <th>Status</th>
          <th>Rationale</th>
          <th>Actions</th>
        </tr>
      </thead>
      <tbody>
        <tr *ngFor="let exp of experiments" [class]="exp.status">
          <td>{{ exp.houndName }}</td>
          <td>{{ exp.timestamp | date:'medium' }}</td>
          <td>{{ exp.baselineScore | number:'1.3-3' }}</td>
          <td>{{ exp.candidateScore | number:'1.3-3' }}</td>
          <td [class]="deltaClass(exp.delta)">{{ exp.delta >= 0 ? '+' : '' }}{{ exp.delta | number:'1.3-3' }}</td>
          <td><span class="badge" [class]="exp.status">{{ exp.status }}</span></td>
          <td class="rationale">{{ exp.rationale }}</td>
          <td class="actions">
            <ng-container *ngIf="exp.status === 'pending-review'">
              <button class="btn-apply" (click)="apply(exp)">Apply</button>
              <button class="btn-reject" (click)="reject(exp)">Reject</button>
            </ng-container>
          </td>
        </tr>
      </tbody>
    </table>

    <p *ngIf="experiments.length === 0" class="empty">No experiments found.</p>

    <div class="pagination" *ngIf="totalCount > pageSize">
      <button [disabled]="page === 1" (click)="prevPage()">Prev</button>
      <span>Page {{ page }}</span>
      <button [disabled]="page * pageSize >= totalCount" (click)="nextPage()">Next</button>
    </div>
  `,
  styles: [`
    .error-msg { color: #721c24; background: #f8d7da; padding: 0.5rem 1rem; border-radius: 4px; margin-bottom: 1rem; }
    h1 { margin-bottom: 1rem; }
    .toolbar { display: flex; align-items: center; margin-bottom: 1rem; gap: 1rem; }
    .count { font-size: 0.9rem; color: #666; }
    .experiments-table { width: 100%; border-collapse: collapse; font-size: 0.9rem; }
    .experiments-table th, .experiments-table td { padding: 0.5rem 0.75rem; border-bottom: 1px solid #eee; text-align: left; }
    .experiments-table th { background: #f5f5f5; font-weight: 600; }
    .rationale { max-width: 300px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .actions { white-space: nowrap; }
    .badge { padding: 2px 8px; border-radius: 4px; font-size: 0.75rem; text-transform: capitalize; }
    .badge.improved { background: #d4edda; color: #155724; }
    .badge.worse { background: #f8d7da; color: #721c24; }
    .badge.crash { background: #f8d7da; color: #721c24; font-weight: bold; }
    .badge.equal { background: #e2e3e5; color: #383d41; }
    .badge.pending-review { background: #fff3cd; color: #856404; }
    .badge.applied { background: #cce5ff; color: #004085; }
    .badge.rejected { background: #e2e3e5; color: #383d41; }
    .delta-positive { color: #155724; font-weight: 600; }
    .delta-negative { color: #721c24; font-weight: 600; }
    .delta-neutral { color: #383d41; }
    .btn-apply { padding: 0.25rem 0.6rem; background: #28a745; color: #fff; border: none; border-radius: 4px; cursor: pointer; margin-right: 0.25rem; font-size: 0.8rem; }
    .btn-reject { padding: 0.25rem 0.6rem; background: #dc3545; color: #fff; border: none; border-radius: 4px; cursor: pointer; font-size: 0.8rem; }
    .btn-apply:hover { background: #218838; }
    .btn-reject:hover { background: #c82333; }
    .pagination { display: flex; align-items: center; gap: 1rem; margin-top: 1rem; }
    .pagination button { padding: 0.3rem 0.75rem; border: 1px solid #ddd; border-radius: 4px; cursor: pointer; background: #fff; }
    .pagination button:disabled { opacity: 0.4; cursor: not-allowed; }
    .empty { color: #666; font-style: italic; }
  `]
})
export class TunerComponent implements OnInit {
  experiments: TunerExperiment[] = [];
  totalCount = 0;
  page = 1;
  readonly pageSize = 20;
  error: string | null = null;

  constructor(private tuner: TunerService) {}

  ngOnInit(): void {
    this.loadExperiments();
  }

  loadExperiments(): void {
    this.error = null;
    this.tuner.getExperiments(this.page, this.pageSize).subscribe({
      next: result => {
        this.experiments = result.items;
        this.totalCount = result.totalCount;
      },
      error: () => {
        this.error = 'Failed to load experiments. Please try again.';
      },
    });
  }

  apply(exp: TunerExperiment): void {
    this.tuner.applyExperiment(exp.id).subscribe({
      next: () => {
        exp.status = 'applied';
      },
      error: () => {
        this.error = `Failed to apply experiment ${exp.id}.`;
      },
    });
  }

  reject(exp: TunerExperiment): void {
    this.tuner.rejectExperiment(exp.id).subscribe({
      next: () => {
        exp.status = 'rejected';
      },
      error: () => {
        this.error = `Failed to reject experiment ${exp.id}.`;
      },
    });
  }

  prevPage(): void {
    if (this.page > 1) {
      this.page--;
      this.loadExperiments();
    }
  }

  nextPage(): void {
    if (this.page * this.pageSize < this.totalCount) {
      this.page++;
      this.loadExperiments();
    }
  }

  deltaClass(delta: number): string {
    if (delta > 0) return 'delta-positive';
    if (delta < 0) return 'delta-negative';
    return 'delta-neutral';
  }
}
