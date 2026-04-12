import { Routes } from '@angular/router';
import { DashboardComponent } from './pages/dashboard/dashboard.component';
import { PackDetailComponent } from './pages/pack-detail/pack-detail.component';
import { ActivityLogComponent } from './pages/activity-log/activity-log.component';
import { TunerComponent } from './pages/tuner/tuner.component';

export const routes: Routes = [
  { path: '', component: DashboardComponent },
  { path: 'packs/:id', component: PackDetailComponent },
  { path: 'activity', component: ActivityLogComponent },
  { path: 'tuner', component: TunerComponent },
];
