import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { HlmToaster } from '@spartan-ng/helm/sonner';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, HlmToaster],
  template: `
    <nav class="flex flex-wrap items-center gap-4 border-b border-border bg-card px-4 py-3 sm:gap-6 sm:px-6">
      <a routerLink="/" class="brand text-lg font-bold text-foreground no-underline">Hound AI</a>
      <div class="flex flex-wrap gap-3 sm:gap-4">
        <a routerLink="/" routerLinkActive="text-primary" [routerLinkActiveOptions]="{ exact: true }"
           class="text-sm text-muted-foreground no-underline transition-colors hover:text-foreground sm:text-base">Dashboard</a>
        <a routerLink="/activity" routerLinkActive="text-primary"
           class="text-sm text-muted-foreground no-underline transition-colors hover:text-foreground sm:text-base">Activity</a>
        <a routerLink="/execution" routerLinkActive="text-primary"
           class="text-sm text-muted-foreground no-underline transition-colors hover:text-foreground sm:text-base">Execution</a>
        <a routerLink="/tuner" routerLinkActive="text-primary"
           class="text-sm text-muted-foreground no-underline transition-colors hover:text-foreground sm:text-base">Tuner</a>
        <a routerLink="/watchtower" routerLinkActive="text-primary"
           class="text-sm text-muted-foreground no-underline transition-colors hover:text-foreground sm:text-base">Watchtower</a>
      </div>
    </nav>
    <main class="mx-auto max-w-7xl p-4 sm:p-6">
      <router-outlet />
    </main>
    <hlm-toaster />
  `,
  styles: []
})
export class App {}
