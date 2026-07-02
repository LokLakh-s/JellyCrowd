# Jelly Crowd — Playwright smoke e2e

A single full-stack smoke test: sign in to Jellyfin, open the Jelly Crowd catalog (populated from TMDB),
and open a title's detail modal. It exercises what only breaks in a real browser against a live server —
header injection, the web shell, the catalog API and the modal.

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
