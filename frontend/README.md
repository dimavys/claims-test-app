# Claims UI (Angular)

The frontend of the Claims Management System. **Project documentation, setup and the specification traceability table
are in the [root README](../README.md).**

```bash
npm ci
npm start                    # http://localhost:4200 (needs the API on http://localhost:5080, see root README)
npx ng test --no-watch       # Vitest unit tests
npm run build                # production build -> dist/claims-ui/browser
```

- Runtime configuration: [`public/config.json`](public/config.json) (`apiBaseUrl`), read at start-up so one build can target any API.
- Source layout: `src/app/core` (typed API services, auth, HTTP interceptors, models), `shared`, `layout`, `features/*` (lazy-loaded).
- Theme: custom Material palette in [`src/theme`](src/theme).
