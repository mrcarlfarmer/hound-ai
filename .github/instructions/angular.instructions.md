---
description: "Use when writing Angular components, services, templates, or tests in the dashboard. Covers standalone components, vitest patterns, SignalR integration, and SCSS styling."
applyTo: "ui/**"
---
# Angular Dashboard Conventions

## Components
- **Standalone only** — every component uses `standalone: true` with explicit `imports`
- Inline `template` and `styles` for small components; separate files for large ones
- Use `CommonModule` for directives (`*ngFor`, `*ngIf`, pipes)
- Use `RouterLink` / `RouterOutlet` from `@angular/router` — import in each component

```typescript
@Component({
  selector: 'app-example',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `...`,
  styles: [`...`]
})
export class ExampleComponent {}
```

## Services
- `@Injectable({ providedIn: 'root' })` for all services
- API base URL: `http://localhost:5000` (proxied in dev via `proxy.conf.json`)
- Return `Observable<T>` from HTTP methods — let components subscribe
- Use `HttpParams` for query string construction

## SignalR
- Hub URL: `http://localhost:5000/hubs/activity`
- Use `@microsoft/signalr` `HubConnectionBuilder` with `.withAutomaticReconnect()`
- Expose events as `Observable` via RxJS `Subject`
- Hub events: `OnActivity`, `OnWatchtowerEvent`
- Hub methods: `SubscribeToPack`, `UnsubscribeFromPack`

## Models
- Interfaces in `src/app/models/index.ts` — re-export from barrel file
- Mirror API contract types: `Pack`, `HoundInfo`, `ActivityLog`, `PagedResult<T>`
- Use union string literals for enums: `'Idle' | 'Running' | 'Error'`

## Testing (vitest + jsdom)
- Test files: `*.spec.ts` next to source files
- Use `vi.fn()` and `vi.mock()` — NOT jasmine spies
- Mock services via `TestBed.overrideProvider()` or `{ provide: X, useValue: mockX }`
- Mock SignalR with `vi.hoisted()` + `vi.mock('@microsoft/signalr', ...)`
- `provideHttpClientTesting()` + `HttpTestingController` for HTTP tests

```typescript
beforeEach(async () => {
  await TestBed.configureTestingModule({
    imports: [MyComponent],
    providers: [provideRouter([]), { provide: ApiService, useValue: mockApi }],
  }).compileComponents();
});
```

## UI Component Library — Spartan-ng
- Uses `@spartan-ng/brain` (headless behavior) + `@spartan-ng/helm` (styled primitives)
- Primitives live in `src/app/components/ui/` — alert, badge, button, card, dialog, icon, input, label, pagination, scroll-area, select, separator, skeleton, sonner (toasts), table, tabs, tooltip
- Import Helm directives from `@spartan-ng/helm/<component>` (e.g., `@spartan-ng/helm/button`)
- Use existing primitives before creating custom components
- `HlmToaster` registered in root `AppComponent` for toast notifications

## Styling
- SCSS (configured in `angular.json`)
- 2-space indentation
