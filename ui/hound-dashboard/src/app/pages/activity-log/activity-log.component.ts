import { Component, OnInit, ChangeDetectorRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { ActivityLog, ActivityFilter } from '../../models';

@Component({
  selector: 'app-activity-log',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './activity-log.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: [],
})
export class ActivityLogComponent implements OnInit {
  activities: ActivityLog[] = [];
  filter: ActivityFilter = {};
  loading = false;

  constructor(
    private api: ApiService,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.loadActivity();
  }

  loadActivity(): void {
    this.loading = true;
    this.api.getActivity(this.filter).subscribe((result) => {
      this.activities = result.items;
      this.loading = false;
      this.cdr.detectChanges();
    });
  }
}
