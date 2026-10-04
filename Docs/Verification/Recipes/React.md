# Verifying a generated React front end

1. Start the API on the scratch copy (see Api.md) on the port the Vite proxy points at (`server.proxy` in `vite.config.ts`, 5173 for the dev server).
2. `npm install` once, then `npm run dev` in the front-end folder; `npm run build` and `npm test` are the quick checks that need no browser.
3. In the browser (the app's built-in browser, or Chrome), drive it with `Browser.md`: open a list page, add a row, edit it, search, sort, delete, open a detail / master dialog.
4. Look for what a build cannot see: a number box that accepts a value the database refuses (min / max from a CHECK range or `NonNegativeColumns`), a drop-down that is blank (a lookup that did not load), a date shown a day off.
5. Stop the dev server and the API; drop the scratch copy.
