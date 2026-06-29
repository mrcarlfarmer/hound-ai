import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent),
  },
  {
    path: 'packs/:id',
    loadComponent: () =>
      import('./pages/pack-detail/pack-detail.component').then(m => m.PackDetailComponent),
  },
  {
    path: 'activity',
    loadComponent: () =>
      import('./pages/activity-log/activity-log.component').then(m => m.ActivityLogComponent),
  },
  {
    path: 'execution',
    loadComponent: () =>
      import('./pages/execution/execution.component').then(m => m.ExecutionComponent),
  },
  {
    path: 'graph',
    loadComponent: () =>
      import('./pages/graph-runs/graph-runs.component').then(m => m.GraphRunsComponent),
  },
  {
    path: 'portfolio',
    loadComponent: () =>
      import('./pages/portfolio/portfolio.component').then(m => m.PortfolioComponent),
  },
  {
    path: 'charts',
    loadComponent: () =>
      import('./pages/charts/charts.component').then(m => m.ChartsComponent),
  },
];
