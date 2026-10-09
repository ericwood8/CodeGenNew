# Verifying a generated Angular front end

1. Start the API on the scratch copy (see Api.md); `proxy.conf.json` in the front-end folder says which port `/api` goes to (it strips nothing: the API routes start with `/api`).
2. `npm install` once; `ng serve --proxy-config proxy.conf.json` (the `start` script) and open `http://localhost:4200`. `npm run build` and `npm test -- --watch=false` need no browser.
3. Drive it with `Browser.md`; the Material components need `provideAnimationsAsync()` or the page renders blank (the build does not warn).
4. Check the same list as for React: limits, drop-downs, dates, delete of a row in use.
5. Stop the servers; drop the scratch copy.
6. For a project on another Angular version run `Test-AngularVersion.ps1 -Version <n>`: it installs, builds and tests the generated app for that version (the `AngularVersion` setting).
