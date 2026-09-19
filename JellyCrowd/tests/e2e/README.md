# Jelly Crowd — Playwright e2e

Two specs, with different needs:

- **`smoke.spec.js`** — full stack: sign in to Jellyfin, open the Jelly Crowd catalog (populated from
  TMDB), and open a title's detail modal. It exercises what only breaks in a real browser against a live
  server — header injection, the web shell, the catalog API and the modal. **Needs a live instance.**
- **`panel-open.spec.js`** — opening a panel, driven through the real `header.js` on a stub Jellyfin
  shell. **Needs no server.** It covers the overlay laying itself out when a view is the first one opened
  in a session, and the panel surviving the Home navigation it triggers itself however late the client
  reports it.
- **`guide-layout.spec.js`** — the user guide, mounted in a faithful copy of the overlay panel and served
  under the production Content-Security-Policy. **Needs no server**: it starts its own on a free port and
  serves `Web/` straight from the repo. It covers the two ways the guide has already broken — screenshots
  refused by a CSP that forbids `data:` images, and the language switch colliding with the panel's close
  button on narrow windows — plus the style isolation the iframe used to provide for free.

```bash
npx playwright test panel-open.spec.js guide-layout.spec.js   # no Jellyfin required
```

It runs against a **live** Jellyfin instance with the plugin installed (see [`dev-stack/`](../../dev-stack)),
so it is **not** part of the per-push CI. Run it locally, or on demand via the **E2E smoke (Playwright)**
workflow (`workflow_dispatch`).

## Run locally

```bash
cd JellyCrowd/tests/e2e
npm ci
npx playwright install chromium   # first time only
npx playwright test
```

Configuration (environment variables, all optional):

| Variable      | Default                  | Purpose                                  |
|---------------|--------------------------|------------------------------------------|
| `JC_BASE_URL` | `http://127.0.0.1:8096`  | Jellyfin base URL                        |
| `JC_USER`     | `Testor`                 | Login username                           |
| `JC_PASSWORD` | `Test101!`               | Login password                           |

Deploy the current build to the dev stack first with [`dev-stack/deploy-plugin.sh`](../../dev-stack/deploy-plugin.sh).
