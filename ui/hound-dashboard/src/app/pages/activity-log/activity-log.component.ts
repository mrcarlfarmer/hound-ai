import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { ActivityLog, ActivityFilter } from '../../models';

@Component({
  selector: 'app-activity-log',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <h1 class="mb-6 font-heading text-3xl font-semibold tracking-tight text-foreground">Activity Log</h1>

    <div class="mb-4 flex gap-2">
      <input [(ngModel)]="filter.pack" placeholder="Pack ID"
             class="rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring" />
      <input [(ngModel)]="filter.hound" placeholder="Hound ID"
             class="rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring" />
      <button (click)="loadActivity()"
              class="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/80">Search</button>
    </div>

    <div class="overflow-hidden rounded-lg border border-border">
      <table class="w-full text-left text-sm">
        <thead>
          <tr class="border-b border-border bg-secondary/50">
            <th class="px-4 py-3 font-semibold text-foreground">Timestamp</th>
            <th class="px-4 py-3 font-semibold text-foreground">Pack</th>
            <th class="px-4 py-3 font-semibold text-foreground">Hound</th>
            <th class="px-4 py-3 font-semibold text-foreground">Message</th>
            <th class="px-4 py-3 font-semibold text-foreground">Severity</th>
          </tr>
        </thead>
        <tbody>
          <tr *ngFor="let item of activities" class="border-b border-border last:border-b-0">
            <td class="px-4 py-3 text-muted-foreground">{{ item.timestamp | date:'medium' }}</td>
            <td class="px-4 py-3 text-foreground">{{ item.packId }}</td>
            <td class="px-4 py-3 text-foreground">{{ item.houndName }}</td>
            <td class="px-4 py-3 text-foreground">{{ item.message }}</td>
            <td class="px-4 py-3">
              <span class="badge inline-block rounded-md px-2 py-0.5 text-xs font-medium"
                    [ngClass]="{
                      'bg-red-900/40 text-red-400': item.severity.toLowerCase() === 'error',
                      'bg-yellow-900/40 text-yellow-400': item.severity.toLowerCase() === 'warning',
                      'bg-green-900/40 text-green-400': item.severity.toLowerCase() === 'success',
                      'bg-blue-900/40 text-blue-400': item.severity.toLowerCase() === 'info'
                    }"
                    [class]="item.severity.toLowerCase()">{{ item.severity }}</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <p *ngIf="activities.length === 0" class="empty mt-4 italic text-muted-foreground">No activity found.</p>
  `,
  styles: []
})
export class ActivityLogComponent implements OnInit {
  activities: ActivityLog[] = [];
  filter: ActivityFilter = {};

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadActivity();
  }

  loadActivity(): void {
    this.api.getActivity(this.filter).subscribe(result => {
      this.activities = result.items;
    });
  }
}
