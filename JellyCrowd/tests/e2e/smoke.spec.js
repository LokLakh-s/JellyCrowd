'use strict';

// Full-stack smoke: sign in to Jellyfin, open the Jelly Crowd catalog (populated from TMDB), and open a
// title's detail modal. Exercises header injection, the web shell, the catalog API and the modal — the
// things that only break in a real browser against a live server. Run against the dev stack (see dev-stack/).

const { test, expect } = require('@playwright/test');

const USER = process.env.JC_USER || 'Testor';
const PASSWORD = process.env.JC_PASSWORD || 'Test101!';

test('sign in, render the TMDB catalog, and open a detail modal', async ({ page }) => {
  await page.goto('/web/index.html');

  // Jellyfin manual login form.
  await page.locator('#txtManualName').waitFor({ state: 'visible' });
  await page.fill('#txtManualName', USER);
  await page.fill('#txtManualPassword', PASSWORD);
  await page.locator('.button-submit').first().click();

  // Wait for login to complete (the manual form goes away) before touching the plugin UI, so the catalog's
  // authenticated TMDB calls succeed.
  await expect(page.locator('#txtManualName')).toBeHidden({ timeout: 30000 });

  // Jelly Crowd injects its own nav into the Jellyfin header (an <a> in the 12 layout, a .jcHeaderTab
  // button in the 10.11 layout — target whichever is visible). "Catalog" is unique among the tabs.
  const catalogTab = page.locator(':is(a, button.jcHeaderTab):visible', { hasText: 'Catalog' }).first();
  await expect(catalogTab).toBeVisible({ timeout: 30000 });
  await catalogTab.click();

  // The catalog feed is populated from TMDB — at least one card must render end-to-end.
  const firstCard = page.locator('.jellycrowd-card').first();
  await expect(firstCard).toBeVisible({ timeout: 30000 });

  // Opening a title shows the details modal (role=dialog), with its close button focusable.
  await firstCard.click();
  const modal = page.locator('.jellycrowd-modal[role="dialog"]');
  await expect(modal).toBeVisible({ timeout: 15000 });
  await expect(page.locator('.jellycrowd-modal-close')).toBeVisible();

  // Escape closes it again.
  await page.keyboard.press('Escape');
  await expect(modal).toBeHidden({ timeout: 10000 });
});
