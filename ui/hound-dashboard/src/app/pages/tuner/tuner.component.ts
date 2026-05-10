import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TunerService } from '../../services/tuner.service';
import { TunerExperiment } from '../../models';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-tuner',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './tuner.component.html',
  styles: []
})
export class TunerComponent implements OnInit {
  experiments: TunerExperiment[] = [];
  totalCount = 0;
  page = 1;
  readonly pageSize = 20;
  error: string | null = null;
  loading = false;

  constructor(private tuner: TunerService) {}

  ngOnInit(): void {
    this.loadExperiments();
  }

  loadExperiments(): void {
    this.error = null;
    this.loading = true;
    this.tuner.getExperiments(this.page, this.pageSize).subscribe({
      next: result => {
        this.experiments = result.items;
        this.totalCount = result.totalCount;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load experiments. Please try again.';
        this.loading = false;
      },
    });
  }

  apply(exp: TunerExperiment): void {
    this.tuner.applyExperiment(exp.id).subscribe({
      next: () => {
        exp.status = 'applied';
        toast.success('Experiment applied successfully.');
      },
      error: () => {
        this.error = `Failed to apply experiment ${exp.id}.`;
        toast.error(`Failed to apply experiment ${exp.id}.`);
      },
    });
  }

  reject(exp: TunerExperiment): void {
    this.tuner.rejectExperiment(exp.id).subscribe({
      next: () => {
        exp.status = 'rejected';
        toast.success('Experiment rejected.');
      },
      error: () => {
        this.error = `Failed to reject experiment ${exp.id}.`;
        toast.error(`Failed to reject experiment ${exp.id}.`);
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
