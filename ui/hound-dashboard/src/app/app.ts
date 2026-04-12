import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { HlmToaster } from '@spartan-ng/helm/sonner';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, HlmToaster],
  template: `
    <nav class="flex items-center gap-6 border-b border-border bg-card px-6 py-3">
      <a routerLink="/" class="brand text-lg font-bold text-foreground no-underline">Hound AI</a>
      <div class="flex gap-4">
        <a routerLink="/" routerLinkActive="text-primary" [routerLinkActiveOptions]="{ exact: true }"
           class="text-muted-foreground no-underline transition-colors hover:text-foreground">Dashboard</a>
        <a routerLink="/activity" routerLinkActive="text-primary"
           class="text-muted-foreground no-underline transition-colors hover:text-foreground">Activity</a>
        <a routerLink="/tuner" routerLinkActive="text-primary"
           class="text-muted-foreground no-underline transition-colors hover:text-foreground">Tuner</a>
        <a routerLink="/watchtower" routerLinkActive="text-primary"
           class="text-muted-foreground no-underline transition-colors hover:text-foreground">Watchtower</a>
      </div>
    </nav>
    <main class="mx-auto max-w-7xl p-6">
      <router-outlet />
    </main>
    <hlm-toaster />
  `,
  styles: []
})
export class App {}
