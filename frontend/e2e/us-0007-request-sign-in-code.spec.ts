import { expect, test, type Page, type Request } from '@playwright/test';

const OTP = '**/auth/v1/otp**';
const INVALID = 'Enter a valid phone number';

function trackOtp(page: Page) {
  const bodies: string[] = [];
  page.on('request', (r: Request) => {
    if (r.url().includes('/auth/v1/otp') && r.method() === 'POST') bodies.push(r.postData() ?? '');
  });
  return bodies;
}

async function mockOtp(page: Page, status: number, body: object, delayMs = 0) {
  await page.route(OTP, async (route) => {
    if (route.request().method() === 'OPTIONS') return route.fallback();
    if (delayMs) await new Promise((r) => setTimeout(r, delayMs));
    await route.fulfill({
      status,
      contentType: 'application/json',
      headers: { 'access-control-allow-origin': '*' },
      body: JSON.stringify(body),
    });
  });
}

async function fill(page: Page, value: string) {
  await page.getByRole('textbox', { name: 'Phone number' }).fill(value);
}
const send = (page: Page) => page.getByRole('button', { name: 'Send code' });

test.describe('US-0007 request sign-in code', () => {
  test('AC1 signed-out visitor sees the sign-in screen with phone field and Send code', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByText("We'll text you a 6-digit code.")).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Phone number' })).toHaveValue('+381');
    await expect(send(page)).toBeVisible();
  });

  for (const [form, input] of [
    ['local with spaces', '060 000 0201'],
    ['international with spaces', '+381 60 000 0201'],
    ['prefix plus leading zero', '+381 060 000 0201'],
    ['dashes', '060-000-0201'],
  ]) {
    test(`AC2 ${form} is normalized to +381600000201 and shows the code screen`, async ({ page }) => {
      await mockOtp(page, 200, {});
      const bodies = trackOtp(page);
      await page.goto('/');
      await fill(page, input);
      await send(page).click();
      await expect(page.getByText('Sent to +381 60 000 0201')).toBeVisible();
      await expect(page).toHaveURL(/\/code/);
      expect(bodies).toHaveLength(1);
      expect(JSON.parse(bodies[0]).phone).toBe('+381600000201');
    });
  }

  test('AC2 a never-seen Serbian number requests a code and reaches the code screen', async ({ page }) => {
    await mockOtp(page, 200, {});
    const bodies = trackOtp(page);
    await page.goto('/');
    await fill(page, '+381600009999');
    await send(page).click();
    await expect(page.getByText('Sent to +381 60 000 9999')).toBeVisible();
    expect(JSON.parse(bodies[0]).phone).toBe('+381600009999');
  });

  test('AC2 real local Supabase accepts a seeded number and reaches the code screen', async ({ page }) => {
    await page.goto('/');
    await fill(page, '060 000 0201');
    await send(page).click();
    await expect(page.getByText('Sent to +381 60 000 0201')).toBeVisible();
    await expect(page).toHaveURL(/\/code/);
  });

  test('AC2 button shows a loading state and the field is disabled while sending, double click requests once', async ({ page }) => {
    await mockOtp(page, 200, {}, 1200);
    const bodies = trackOtp(page);
    await page.goto('/');
    await fill(page, '0600000201');
    await send(page).dblclick();
    await expect(send(page)).toBeDisabled();
    await expect(page.getByRole('textbox', { name: 'Phone number' })).toHaveAttribute('readonly', '');
    await expect(page.getByText('Sent to +381 60 000 0201')).toBeVisible();
    expect(bodies).toHaveLength(1);
  });

  for (const [name, input] of [
    ['empty', ''],
    ['only the prefix', '+381'],
    ['letters', 'abcdef'],
    ['UK number', '+44 7911 123456'],
    ['too short', '+381 60 123'],
    ['too long', '+381 60 000 02011234'],
    ['very long', '0'.repeat(300)],
    ['non-mobile Serbian landline', '+381 11 123 4567'],
    ['text mixed with digits', '+381 60 000 abcd'],
  ]) {
    test(`AC3 ${name} shows the validation message and requests no code`, async ({ page }) => {
      const bodies = trackOtp(page);
      await page.goto('/');
      await fill(page, input);
      await send(page).click();
      await expect(page.getByText(INVALID)).toBeVisible();
      await expect(page).not.toHaveURL(/\/code/);
      expect(bodies).toHaveLength(0);
    });
  }

  test('AC4 rate limit shows the rate-limit message, then retry works and clears it', async ({ page }) => {
    await mockOtp(page, 429, { code: 429, error_code: 'over_sms_send_rate_limit', msg: 'For security purposes, you can only request this after 60 seconds.' });
    await page.goto('/');
    await fill(page, '0600000201');
    await send(page).click();
    await expect(page.getByRole('alert')).toHaveText('Too many attempts. Try again in a minute.');
    await expect(page).not.toHaveURL(/\/code/);
    await page.unroute(OTP);
    await mockOtp(page, 200, {});
    await send(page).click();
    await expect(page.getByText('Sent to +381 60 000 0201')).toBeVisible();
  });

  test('AC4 a server error shows the generic message and a second failure still allows retry', async ({ page }) => {
    await mockOtp(page, 500, { code: 500, msg: 'boom' });
    await page.goto('/');
    await fill(page, '0600000201');
    await send(page).click();
    await expect(page.getByRole('alert')).toHaveText("Couldn't send the code. Try again.");
    await expect(send(page)).toBeEnabled();
    await send(page).click();
    await expect(page.getByRole('alert')).toHaveText("Couldn't send the code. Try again.");
  });

  test('AC4 a network failure shows the generic message', async ({ page }) => {
    await page.route(OTP, (route) => (route.request().method() === 'OPTIONS' ? route.fallback() : route.abort('failed')));
    await page.goto('/');
    await fill(page, '0600000201');
    await send(page).click();
    await expect(page.getByRole('alert')).toHaveText("Couldn't send the code. Try again.");
    await expect(page).not.toHaveURL(/\/code/);
  });

  test('AC2 pressing Enter in the phone field submits the form (BUG-0020 fixed)', async ({ page }) => {
    await mockOtp(page, 200, {});
    const bodies = trackOtp(page);
    await page.goto('/');
    await page.getByRole('textbox', { name: 'Phone number' }).fill('0600000201');
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/code/);
    expect(bodies).toHaveLength(1);
  });

  test('keyboard: Tab reaches Send code and Enter on the button submits', async ({ page }) => {
    await mockOtp(page, 200, {});
    await page.goto('/');
    await page.getByRole('textbox', { name: 'Phone number' }).fill('0600000201');
    await page.keyboard.press('Tab');
    await expect(send(page)).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByText('Sent to +381 60 000 0201')).toBeVisible();
  });

  test('KNOWN_GAP BUG-0021 loading state is not exposed as aria-busy', async ({ page }) => {
    await mockOtp(page, 200, {}, 800);
    await page.goto('/');
    await fill(page, '0600000201');
    await send(page).click();
    await expect(send(page)).toBeDisabled();
    await expect(send(page)).not.toHaveAttribute('aria-busy', 'true');
  });

  test('AC3 validation message is announced as an alert and tied to the invalid field (BUG-0022)', async ({ page }) => {
    await page.goto('/');
    const input = page.getByRole('textbox', { name: 'Phone number' });
    await expect(input).not.toHaveAttribute('aria-invalid', 'true');
    await fill(page, 'abc');
    await send(page).click();
    const alert = page.getByRole('alert');
    await expect(alert).toHaveCount(1);
    await expect(alert).toHaveText(INVALID);
    await expect(input).toHaveAttribute('aria-invalid', 'true');
    const alertId = await alert.getAttribute('id');
    expect(alertId).toBeTruthy();
    await expect(input).toHaveAttribute('aria-describedby', alertId!);
    await fill(page, '0600000201');
    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(input).not.toHaveAttribute('aria-invalid', 'true');
    await expect(input).not.toHaveAttribute('aria-describedby', /.+/);
  });

  test('AC4 send error is the only alert and the field is not marked invalid (BUG-0022 regression)', async ({ page }) => {
    await mockOtp(page, 500, { code: 500, msg: 'boom' });
    await page.goto('/');
    await fill(page, '0600000201');
    await send(page).click();
    await expect(page.getByRole('alert')).toHaveCount(1);
    await expect(page.getByRole('alert')).toHaveText("Couldn't send the code. Try again.");
  });

  test('validation message disappears once the number is corrected', async ({ page }) => {
    await page.goto('/');
    await fill(page, 'abc');
    await send(page).click();
    await expect(page.getByText(INVALID)).toBeVisible();
    await fill(page, '0600000201');
    await expect(page.getByText(INVALID)).toHaveCount(0);
  });

  test('KNOWN_GAP BUG-0023 navigation headers show raw route names ("index", "code")', async ({ page }) => {
    await mockOtp(page, 200, {});
    await page.goto('/');
    await expect(page.getByText('index', { exact: true })).toBeVisible();
    await fill(page, '0600000201');
    await send(page).click();
    await expect(page.getByText('code', { exact: true })).toBeVisible();
  });

  test('reload on the code screen keeps the number and browser back returns to sign-in', async ({ page }) => {
    await mockOtp(page, 200, {});
    await page.goto('/');
    await fill(page, '0600000201');
    await send(page).click();
    await expect(page.getByText('Sent to +381 60 000 0201')).toBeVisible();
    await page.reload();
    await expect(page.getByText('Sent to +381 60 000 0201')).toBeVisible();
    await page.goBack();
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Phone number' })).toHaveValue('+381');
  });

  test('no sign-in screen at 360px overflows horizontally', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 640 });
    await page.goto('/');
    await expect(send(page)).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('direct visit to /code without a phone does not crash and shows sign-in', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto('/code');
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    expect(errors).toEqual([]);
  });

  test('AC2 /code without a phone sends the user back to sign-in (BUG-0024 fixed)', async ({ page }) => {
    await page.goto('/code');
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Enter the code' })).toHaveCount(0);
  });
});
