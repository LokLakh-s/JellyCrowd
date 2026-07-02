'use strict';

const { defineConfig } = require('@playwright/test');

// Smoke e2e against a LIVE Jellyfin dev stack (see dev-stack/). Not part of per-push CI — run locally or
// via the nightly workflow. Point it at an instance with the plugin installed and a known user:
//   JC_BASE_URL (default http://127.0.0.1:8096), JC_USER (default Testor), JC_PASSWORD (default Test101!).
module.exports = defineConfig({
  testDir: '.',
  timeout: 90000,
  expect: { timeout: 15000 },
  retries: 0,
  reporter: [['line']],
  use: {
    baseURL: process.env.JC_BASE_URL || 'http://127.0.0.1:8096',
    headless: true,
    ignoreHTTPSErrors: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure'
  }
});
