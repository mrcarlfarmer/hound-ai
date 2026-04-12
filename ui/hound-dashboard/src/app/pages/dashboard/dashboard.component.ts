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
    <h1 class="mb-6 font-heading text-3xl font-semibold tracking-tight text-foreground">Hound AI Dashboard</h1>
    <div class="grid grid-cols-[repeat(auto-fill,minmax(250px,1fr))] gap-4">
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
    <p *ngIf="packs.length === 0" class="empty mt-4 italic text-muted-foreground">No packs registered.</p>
  `,
  styles: []
})
export class DashboardComponent implements OnInit {
  packs: Pack[] = [];

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.api.getPacks().subscribe(packs => this.packs = packs);
  }
}
