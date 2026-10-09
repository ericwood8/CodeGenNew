# TS_EssentialBuild_v1

The `Build` group of the Angular essentials: `package.json`, `angular.json`, `tsconfig.json`, `tsconfig.app.json` and `tsconfig.spec.json` for the Angular app's own folder (`codegen essentials --stack angular --groups build --project <name> -o <project root>`). No table and no database are needed. `AngularVersion` (18 to 22, default 22) chooses everything below from one table, `AngularVersions` in `CodeGenNew.Core`.

## What each version gets

| Angular | zone.js | TypeScript | builders (`build`, `serve`, `test`) | tests | also |
|---|---|---|---|---|---|
| 18 | `~0.14.10` | `~5.5.4` | `@angular-devkit/build-angular` (`:application`, `:dev-server`, `:karma`) | Karma and Jasmine | `outputPath` and `index` in angular.json (the builder of 18 and 19 asks for both), `module: ES2022` with the bundler resolution |
| 19 | `~0.15.0` | `~5.6.3` | `@angular-devkit/build-angular` | Karma and Jasmine | `outputPath` and `index` in angular.json, `module: ES2022` with the bundler resolution |
| 20 | `~0.15.1` | `~5.8.2` | `@angular/build` (`:application`, `:dev-server`, `:karma`) | Karma and Jasmine | `module: preserve`, `provideBrowserGlobalErrorListeners()` in app.config.ts |
| 21 | `~0.16.0` | `~5.9.2` | `@angular/build` (`:unit-test`) | Vitest and jsdom | |
| 22 | `~0.16.0` | `~6.0.2` | `@angular/build` (`:unit-test`) | Vitest and jsdom | no `@angular/animations`, `Eager` change detection |

`@angular/material` and `@angular/cdk` take the same major version. Up to 21 the app also gets `@angular/animations` (Material's peer), and for Karma on 18 and 19 `@angular/platform-browser-dynamic`. `Toasts=ngx-toastr` adds the package (`^19.1.0` with `provideAnimations()` before 21, `^20.0.5` from 21 with an npm `overrides` entry for 22) and its stylesheet.

## What the other templates write for each version

- **Control flow:** from 18 the generated html uses `@if` / `@for` / `@else`; with no `AngularVersion` it uses `*ngIf` / `*ngFor`, which every version still accepts.
- **Standalone:** components are standalone in every version. Before 19 the decorator says `standalone: true`; from 19 that is the default and the flag is left out.
- **`inject()`:** the services and screens take their dependencies with `inject()` (available since Angular 14), so nothing changes by version.
- **Change detection:** the screens set the eager strategy, spelled `Default` before 22 and `Eager` from 22 (22 made OnPush the default).
- **Specs:** the component specs provide `provideNoopAnimations()` until 22, which no longer has the package. The specs of the bases use only `describe`, `it`, `expect` and `beforeEach`, so they run under Jasmine (Karma) and Vitest alike.

## Checked

`Docs/Verification/Test-AngularVersion.ps1 -Version <n> -Project <name> -ConnectionArgs ...` generates the Angular app for that version into a scratch folder, runs `npm install`, `ng build` and `ng test`. Anything outside 18 to 22 gets the nearest row of the table.
