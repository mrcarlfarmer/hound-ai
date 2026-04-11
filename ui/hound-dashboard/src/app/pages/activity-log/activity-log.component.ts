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
    <h1>Activity Log</h1>

    <div class="filters">
      <input [(ngModel)]="filter.pack" placeholder="Pack ID" />
      <input [(ngModel)]="filter.hound" placeholder="Hound ID" />
      <button (click)="loadActivity()">Search</button>
    </div>

    <table class="activity-table">
      <thead>
        <tr>
          <th>Timestamp</th>
          <th>Pack</th>
          <th>Hound</th>
          <th>Message</th>
          <th>Severity</th>
        </tr>
      </thead>
      <tbody>
        <tr *ngFor="let item of activities" [class]="item.severity.toLowerCase()">
          <td>{{ item.timestamp | date:'medium' }}</td>
          <td>{{ item.packId }}</td>
          <td>{{ item.houndName }}</td>
          <td>{{ item.message }}</td>
          <td><span class="badge" [class]="item.severity.toLowerCase()">{{ item.severity }}</span></td>
        </tr>
      </tbody>
    </table>

    <p *ngIf="activities.length === 0" class="empty">No activity found.</p>
  `,
  styles: [`
    .filters { display: flex; gap: 0.5rem; margin-bottom: 1rem; }
    .filters input { padding: 0.5rem; border: 1px solid #ddd; border-radius: 4px; }
    .filters button { padding: 0.5rem 1rem; background: #333; color: #fff; border: none; border-radius: 4px; cursor: pointer; }
    .activity-table { width: 100%; border-collapse: collapse; }
    .activity-table th, .activity-table td { padding: 0.5rem; border-bottom: 1px solid #eee; text-align: left; }
    .badge { padding: 2px 8px; border-radius: 4px; font-size: 0.8rem; }
    .badge.error { background: #f8d7da; color: #721c24; }
    .badge.warning { background: #fff3cd; color: #856404; }
    .badge.success { background: #d4edda; color: #155724; }
    .badge.info { background: #d1ecf1; color: #0c5460; }
    .empty { color: #666; font-style: italic; }
  `]
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
