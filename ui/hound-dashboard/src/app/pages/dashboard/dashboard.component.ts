import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { Pack } from '../../models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <h1>Hound AI Dashboard</h1>
    <div class="pack-grid">
      <div *ngFor="let pack of packs" class="pack-card" [routerLink]="['/packs', pack.id]">
        <h3>{{ pack.name }}</h3>
        <span class="status" [class]="pack.status.toLowerCase()">{{ pack.status }}</span>
        <p>{{ pack.houndCount }} hounds</p>
        <p *ngIf="pack.lastActivity" class="last-activity">Last: {{ pack.lastActivity | date:'short' }}</p>
      </div>
    </div>
    <p *ngIf="packs.length === 0" class="empty">No packs registered.</p>
  `,
  styles: [`
    .pack-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: 1rem; }
    .pack-card { padding: 1rem; border: 1px solid #ddd; border-radius: 8px; cursor: pointer; }
    .pack-card:hover { border-color: #666; }
    .status { padding: 2px 8px; border-radius: 4px; font-size: 0.85rem; }
    .status.running { background: #d4edda; color: #155724; }
    .status.idle { background: #e2e3e5; color: #383d41; }
    .status.error { background: #f8d7da; color: #721c24; }
    .empty { color: #666; font-style: italic; }
  `]
})
export class DashboardComponent implements OnInit {
  packs: Pack[] = [];

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.api.getPacks().subscribe(packs => this.packs = packs);
  }
}
