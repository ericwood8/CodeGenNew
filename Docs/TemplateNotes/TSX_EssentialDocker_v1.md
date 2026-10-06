# TSX_EssentialDocker_v1 and TS_EssentialDocker_v1

The optional `Docker` essentials group of the React and Angular stacks: `Dockerfile`, `nginx.conf.template` and `.dockerignore` in the front end's own folder. Not ticked by default; run `codegen essentials --stack react --groups docker` (or `--stack angular`), or tick it in the Essentials dialog.

- **Image:** a Node stage (`node:24-alpine`) runs `npm install` and `npm run build`; an nginx stage (`nginx:alpine`) serves the result (`dist` for React, `dist/frontend/browser` for Angular, the application builder's output for the project named `frontend` in `angular.json`). No package is added to the app and no secret is written.
- **The API:** the generated api modules and services call relative `/api/` paths. nginx forwards them to the host in the `API_UPSTREAM` environment variable (default `api:8080`, the port the API's `Dockerfile` from `ApiProduction=true` listens on), using the official image's template substitution. Any other path answers `index.html`, so a router URL survives a reload.
- **Checked:** both images were built and run beside the API image on one Docker network over the SQLite shop sample: the front page, a router path and `/api/customers/1` through nginx all answered 200, and the API ran as a non-root user. The `docker-compose.yml` and the CI workflow of item 66 are not written yet.
- The React group needs `src/vite-env.d.ts`, which `TSX_EssentialBuild` now writes; without it `npm run build` fails on `import.meta.env`.
