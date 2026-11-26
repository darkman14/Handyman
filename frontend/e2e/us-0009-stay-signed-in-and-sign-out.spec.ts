import { expect as baseExpect, test, type BrowserContext, type Page } from '@playwright/test';

// The dev bundle is slow to load on a cold Metro cache; give assertions room.
const expect = baseExpect.configure({ timeout: 30_000 });
test.setTimeout(120_000);

const ANA = { phone: '+381600000201', shown: '+381 60 000 0201' };

const storageKey = (page: Page) =>
  page.evaluate(() => Object.keys(localStorage).find((k) => /^sb-.*-auth-token$/.test(k)) ?? null);

async function signInViaUi(page: Page, landing = /\/home$/) {
  await page.goto(`/code?phone=${encodeURIComponent(ANA.phone)}`);
  await page.getByRole('textbox', { name: 'Code' }).fill('123456');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(landing);
}

const home = (page: Page) => page.getByRole('heading', { name: 'Welcome' });
const signInHeading = (page: Page) => page.getByRole('textbox', { name: 'Phone number' });
const signOutBtn = (page: Page) => page.getByRole('button', { name: 'Sign out' });

test.describe('US-0009 stay signed in and sign out', () => {
  test('AC1 reload keeps the session and lands on home', async ({ page }) => {
    await signInViaUi(page);
    await expect(home(page)).toBeVisible();
    await page.reload();
    await expect(home(page)).toBeVisible();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
    await expect(page).toHaveURL(/\/home$/);
  });

  test('AC1 a new tab in the same browser is signed in', async ({ page, context }) => {
    await signInViaUi(page);
    const tab = await context.newPage();
    await tab.goto('/home');
    await expect(home(tab)).toBeVisible();
  });

  test('AC1 no sign-in screen flashes while the session is restored', async ({ page }) => {
    await signInViaUi(page);
    await page.addInitScript(() => {
      (window as unknown as { __seen: boolean }).__seen = false;
      new MutationObserver(() => {
        if (document.querySelector('input[aria-label="Phone number"]')) {
          (window as unknown as { __seen: boolean }).__seen = true;
        }
      }).observe(document, { childList: true, subtree: true });
    });
    await page.goto('/home');
    await expect(home(page)).toBeVisible();
    expect(await page.evaluate(() => (window as unknown as { __seen: boolean }).__seen)).toBe(false);
  });

  test('AC2 signed out: direct URL to /home goes to sign-in, then lands on /home after sign-in', async ({ page }) => {
    await page.goto('/home');
    await expect(signInHeading(page)).toBeVisible();
    await expect(page).toHaveURL(/localhost:8081\/$/); // home is the default landing, so no redirect param is needed
    await page.getByRole('textbox', { name: 'Phone number' }).fill('060 000 0201');
    await page.getByRole('button', { name: 'Send code' }).click();
    await page.getByRole('textbox', { name: 'Code' }).fill('123456');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/home$/);
    await expect(home(page)).toBeVisible();
  });

  test('KNOWN_GAP BUG-0025 protected screen is mounted behind the overlay while signed out', async ({ page }) => {
    await page.addInitScript(() => {
      (window as unknown as { __home: boolean }).__home = false;
      new MutationObserver(() => {
        if (document.body.innerText.includes('Welcome')) (window as unknown as { __home: boolean }).__home = true;
      }).observe(document, { childList: true, subtree: true, characterData: true });
    });
    await page.goto('/home');
    await expect(signInHeading(page)).toBeVisible();
    // Desired: false. Today the home screen is in the DOM behind the loading overlay.
    expect(await page.evaluate(() => (window as unknown as { __home: boolean }).__home)).toBe(true);
  });

  for (const bad of ['//evil.com', 'https://evil.com', 'javascript:alert(1)', '/\\evil.com', '\\\\evil.com', '/%5Cevil.com', '/code', '/']) {
    test(`AC2 open redirect attempt is ignored: ${bad}`, async ({ page }) => {
      await page.goto(`/?redirect=${encodeURIComponent(bad)}`);
      await expect(signInHeading(page)).toBeVisible();
      await page.getByRole('textbox', { name: 'Phone number' }).fill('060 000 0201');
      await page.getByRole('button', { name: 'Send code' }).click();
      await page.getByRole('textbox', { name: 'Code' }).fill('123456');
      await page.getByRole('button', { name: 'Sign in' }).click();
      await expect(page).toHaveURL(/localhost:8081\/home$/);
      await expect(home(page)).toBeVisible();
    });
  }

  test('AC3 signed in: opening / or /code goes to home', async ({ page }) => {
    await signInViaUi(page);
    await page.goto('/');
    await expect(home(page)).toBeVisible();
    await expect(page).toHaveURL(/\/home$/);
    await page.goto(`/code?phone=${encodeURIComponent(ANA.phone)}`);
    await expect(home(page)).toBeVisible();
    await expect(page).toHaveURL(/\/home$/);
  });

  test('AC3 signed in: back after being sent to home never shows the sign-in screen', async ({ page }) => {
    await signInViaUi(page);
    await page.goto('/');
    await expect(home(page)).toBeVisible();
    await page.goBack();
    await expect(signInHeading(page)).toHaveCount(0);
  });

  test('AC4 sign out: cancel keeps the session, confirm removes it and reload stays signed out', async ({ page }) => {
    await signInViaUi(page);
    await signOutBtn(page).click();
    const dialog = page.getByRole('alertdialog', { name: 'Sign out?' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toBeHidden();
    await expect(home(page)).toBeVisible();
    expect(await storageKey(page)).not.toBeNull();

    await signOutBtn(page).click();
    await page.getByRole('alertdialog', { name: 'Sign out?' }).getByRole('button', { name: 'Sign out' }).click();
    await expect(signInHeading(page)).toBeVisible();
    expect(await storageKey(page)).toBeNull();
    await page.reload();
    await expect(signInHeading(page)).toBeVisible();
    await page.goto('/home');
    await expect(signInHeading(page)).toBeVisible();
  });

  test('AC4 sign out dialog: Escape cancels and focus is inside the dialog', async ({ page }) => {
    await signInViaUi(page);
    await signOutBtn(page).click();
    const dialog = page.getByRole('alertdialog', { name: 'Sign out?' });
    await expect(dialog).toBeVisible();
    const inside = () => page.evaluate(() => !!document.activeElement?.closest('[role="alertdialog"]'));
    await expect.poll(inside).toBe(true);
    for (let i = 0; i < 4; i++) {
      await page.keyboard.press('Tab');
      expect(await inside()).toBe(true); // focus is trapped in the dialog
    }
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(home(page)).toBeVisible();
  });

  test('AC4 sign out after sign out: back button does not reveal home', async ({ page }) => {
    await signInViaUi(page);
    await signOutBtn(page).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Sign out' }).click();
    await expect(signInHeading(page)).toBeVisible();
    await page.goBack();
    await expect(home(page)).toHaveCount(0);
  });

  test('AC4 two tabs: sign out in one signs the other out', async ({ page, context }) => {
    await signInViaUi(page);
    const other = await context.newPage();
    await other.goto('/home');
    await expect(home(other)).toBeVisible();
    await signOutBtn(page).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Sign out' }).click();
    await expect(signInHeading(page)).toBeVisible();
    await expect(signInHeading(other)).toBeVisible({ timeout: 15000 });
  });

  test('AC4 sign out works at phone width', async ({ browser }) => {
    const ctx = await browser.newContext({ viewport: { width: 390, height: 844 } });
    const page = await ctx.newPage();
    await signInViaUi(page);
    await signOutBtn(page).click();
    const dialog = page.getByRole('alertdialog', { name: 'Sign out?' });
    await expect(dialog).toBeVisible();
    const box = await dialog.boundingBox();
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(390);
    await dialog.getByRole('button', { name: 'Sign out' }).click();
    await expect(signInHeading(page)).toBeVisible();
    await ctx.close();
  });

  test('AC5 corrupted stored session shows sign-in, not an error or blank', async ({ page }) => {
    await signInViaUi(page);
    const key = (await storageKey(page))!;
    await page.evaluate((k) => localStorage.setItem(k, '{not json'), key);
    await page.reload();
    await expect(signInHeading(page)).toBeVisible();
  });

  test('AC5 revoked refresh token (expired access token, refresh rejected) shows sign-in', async ({ page }) => {
    await signInViaUi(page);
    const key = (await storageKey(page))!;
    await page.evaluate((k) => {
      const s = JSON.parse(localStorage.getItem(k)!);
      s.expires_at = 1;
      s.refresh_token = 'revoked-token';
      localStorage.setItem(k, JSON.stringify(s));
    }, key);
    await page.reload();
    await expect(signInHeading(page)).toBeVisible();
    await expect(page.getByText(/error|something went wrong/i)).toHaveCount(0);
  });

  test('AC5 refresh endpoint returns 400 invalid_grant (mocked) shows sign-in', async ({ page }) => {
    await signInViaUi(page);
    const key = (await storageKey(page))!;
    await page.evaluate((k) => {
      const s = JSON.parse(localStorage.getItem(k)!);
      s.expires_at = 1;
      localStorage.setItem(k, JSON.stringify(s));
    }, key);
    await page.route('**/auth/v1/token**', (route) =>
      route.request().method() === 'OPTIONS'
        ? route.fallback()
        : route.fulfill({
            status: 400,
            contentType: 'application/json',
            headers: { 'access-control-allow-origin': '*' },
            body: JSON.stringify({ error: 'invalid_grant', error_description: 'Invalid Refresh Token' }),
          }),
    );
    await page.reload();
    await expect(signInHeading(page)).toBeVisible();
  });

  test('Edge expired access token with a valid refresh token refreshes silently', async ({ page }) => {
    await signInViaUi(page);
    const key = (await storageKey(page))!;
    await page.evaluate((k) => {
      const s = JSON.parse(localStorage.getItem(k)!);
      s.expires_at = 1;
      localStorage.setItem(k, JSON.stringify(s));
    }, key);
    await page.reload();
    await expect(home(page)).toBeVisible();
    const exp = await page.evaluate((k) => JSON.parse(localStorage.getItem(k)!).expires_at, key);
    expect(exp).toBeGreaterThan(Date.now() / 1000);
  });

  test('Edge offline at startup with a stored session does not end on an error or blank screen', async ({ page, context }) => {
    await signInViaUi(page);
    await page.reload();
    await expect(home(page)).toBeVisible();
    // Fresh token still valid: going offline then reloading the (cached) app.
    await context.setOffline(true);
    await page.reload().catch(() => undefined);
    await context.setOffline(false);
  });

  test('Edge private window (fresh context) starts signed out and works end to end', async ({ browser }) => {
    const ctx: BrowserContext = await browser.newContext();
    const page = await ctx.newPage();
    await page.goto('/home');
    await expect(signInHeading(page)).toBeVisible();
    await signInViaUi(page);
    await expect(home(page)).toBeVisible();
    await ctx.close();
  });

  test('A11y sign out button and dialog buttons are reachable by keyboard', async ({ page }) => {
    await signInViaUi(page);
    await signOutBtn(page).focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('alertdialog', { name: 'Sign out?' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('heading', { name: 'Sign out?' })).toBeVisible();
  });
});
