# HoundDashboard

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 21.2.7.

It now uses Angular 22, TypeScript 6.0, and Spartan 1.9. Use Node 24.22.1
on the 24 LTS line with npm 12.2.1. Run `npm ci` before starting local checks.
Do not force TypeScript 7 or bypass peer dependency resolution.

## Development server

The dashboard normally runs inside the `hound-ui` container started by
`docker compose -f docker-compose.yml -f docker-compose.dev.yml up`. That
container's entrypoint installs npm 12.2.1, runs `npm ci`, and starts `ng serve` with the source
tree bind-mounted from the host and `/app/node_modules` masked by an
anonymous Docker volume.

If you only want the standalone Angular dev server (no API / SignalR
proxy), you can still run it locally:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Adding or upgrading npm packages

The anonymous volume backing `/app/node_modules` is **created once** when the
container first starts and is **not** synced with `npm install` runs on the
host. If you edit `package.json` (add a dep, bump a version, etc.) while the
dev stack is running, the container will keep using its old, frozen
`node_modules` and Angular will fail with `TS2307: Cannot find module …`
— often *silently* compiling a stale bundle that omits any component that
imported the missing dep.

After any `package.json` change, refresh the container so its entrypoint
re-runs `npm ci` against the updated lockfile:

```bash
# Cheap path — entrypoint re-runs `npm ci` against the new lockfile
docker compose -f docker-compose.yml -f docker-compose.dev.yml restart hound-ui

# If needed, recreate only the UI container's anonymous volume
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --force-recreate --renew-anon-volumes --no-deps hound-ui
```

Always hard-reload (`Ctrl + Shift + R`) the browser afterwards to bypass
the cached JS bundle.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
npx ng test --watch=false
```

The suite includes interaction regressions for the migrated select and dialog
primitives. Run `npx ng build --configuration production` as well: unit-test
bundles do not necessarily compile every local Helm primitive.

No separate Angular end-to-end runner is configured. Browser smoke checks should
use an isolated API or fixtures, never real trade or shopping actions.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
