import { Component } from '@angular/core';
import { RouterOutlet, RouterLink } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink],
  template: `
    <nav class="navbar">
      <a routerLink="/" class="brand">Hound AI</a>
      <div class="nav-links">
        <a routerLink="/">Dashboard</a>
        <a routerLink="/activity">Activity</a>
      </div>
    </nav>
    <main class="content">
      <router-outlet />
    </main>
  `,
  styles: [`
    .navbar { display: flex; align-items: center; padding: 0.75rem 1.5rem; background: #1a1a2e; color: #fff; }
    .brand { font-weight: bold; font-size: 1.2rem; color: #fff; text-decoration: none; margin-right: 2rem; }
    .nav-links a { color: #ccc; text-decoration: none; margin-right: 1rem; }
    .nav-links a:hover { color: #fff; }
    .content { padding: 1.5rem; max-width: 1200px; margin: 0 auto; }
  `]
})
export class App {}
