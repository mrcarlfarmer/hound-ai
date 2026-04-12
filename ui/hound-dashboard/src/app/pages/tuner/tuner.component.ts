import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TunerService } from '../../services/tuner.service';
import { TunerExperiment } from '../../models';

@Component({
  selector: 'app-tuner',
  standalone: true,
  imports: [CommonModule],
  template: `
    <h1 class="mb-4 font-heading text-3xl font-semibold tracking-tight text-foreground">Tuner Experiments</h1>

    <div class="mb-4 flex items-center gap-4">
      <span class="text-sm text-muted-foreground" *ngIf="totalCount > 0">{{ totalCount }} experiment(s)</span>
    </div>

    <p *ngIf="error" class="mb-4 rounded-md bg-red-900/40 px-4 py-2 text-sm text-red-400">{{ error }}</p>

    <div class="overflow-hidden rounded-lg border border-border" *ngIf="experiments.length > 0">
      <table class="w-full text-left text-sm">
        <thead>
          <tr class="border-b border-border bg-secondary/50">
            <th class="px-3 py-3 font-semibold text-foreground">Hound</th>
            <th class="px-3 py-3 font-semibold text-foreground">Timestamp</th>
            <th class="px-3 py-3 font-semibold text-foreground">Baseline</th>
            <th class="px-3 py-3 font-semibold text-foreground">Candidate</th>
            <th class="px-3 py-3 font-semibold text-foreground">Delta</th>
            <th class="px-3 py-3 font-semibold text-foreground">Status</th>
            <th class="px-3 py-3 font-semibold text-foreground">Rationale</th>
            <th class="px-3 py-3 font-semibold text-foreground">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr *ngFor="let exp of experiments" class="border-b border-border last:border-b-0">
            <td class="px-3 py-3 text-foreground">{{ exp.houndName }}</td>
            <td class="px-3 py-3 text-muted-foreground">{{ exp.timestamp | date:'medium' }}</td>
            <td class="px-3 py-3 text-foreground">{{ exp.baselineScore | number:'1.3-3' }}</td>
            <td class="px-3 py-3 text-foreground">{{ exp.candidateScore | number:'1.3-3' }}</td>
            <td class="px-3 py-3 font-semibold"
                [ngClass]="{
                  'text-green-400': exp.delta > 0,
                  'text-red-400': exp.delta < 0,
                  'text-muted-foreground': exp.delta === 0
                }">{{ exp.delta >= 0 ? '+' : '' }}{{ exp.delta | number:'1.3-3' }}</td>
            <td class="px-3 py-3">
              <span class="badge inline-block rounded-md px-2 py-0.5 text-xs font-medium capitalize"
                    [ngClass]="{
                      'bg-green-900/40 text-green-400': exp.status === 'improved',
                      'bg-red-900/40 text-red-400': exp.status === 'worse' || exp.status === 'crash',
                      'font-bold': exp.status === 'crash',
                      'bg-secondary text-secondary-foreground': exp.status === 'equal' || exp.status === 'rejected',
                      'bg-yellow-900/40 text-yellow-400': exp.status === 'pending-review',
                      'bg-blue-900/40 text-blue-400': exp.status === 'applied'
                    }">{{ exp.status }}</span>
            </td>
            <td class="max-w-xs truncate px-3 py-3 text-muted-foreground">{{ exp.rationale }}</td>
            <td class="whitespace-nowrap px-3 py-3">
              <ng-container *ngIf="exp.status === 'pending-review'">
                <button class="btn-apply mr-1 rounded-md bg-green-600 px-2.5 py-1 text-xs font-medium text-white transition-colors hover:bg-green-700"
                        (click)="apply(exp)">Apply</button>
                <button class="btn-reject rounded-md bg-red-600 px-2.5 py-1 text-xs font-medium text-white transition-colors hover:bg-red-700"
                        (click)="reject(exp)">Reject</button>
              </ng-container>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <p *ngIf="experiments.length === 0" class="empty mt-4 italic text-muted-foreground">No experiments found.</p>

    <div class="pagination mt-4 flex items-center gap-4" *ngIf="totalCount > pageSize">
      <button [disabled]="page === 1" (click)="prevPage()"
              class="rounded-md border border-border bg-background px-3 py-1.5 text-sm text-foreground transition-colors hover:bg-secondary disabled:cursor-not-allowed disabled:opacity-40">Prev</button>
      <span class="text-sm text-muted-foreground">Page {{ page }}</span>
      <button [disabled]="page * pageSize >= totalCount" (click)="nextPage()"
              class="rounded-md border border-border bg-background px-3 py-1.5 text-sm text-foreground transition-colors hover:bg-secondary disabled:cursor-not-allowed disabled:opacity-40">Next</button>
    </div>
  `,
  styles: []
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
