import { expect, test, type Page, type Request } from '@playwright/test';

const VERIFY = '**/auth/v1/verify**';
const OTP = '**/auth/v1/otp**';
const WRONG = 'That code is wrong or has expired.';
const RATE = 'Too many attempts. Try again in a minute.';
const SEND_FAILED = "Couldn't send the code. Try again.";
const VERIFY_FAILED = "Couldn't sign you in. Try again.";

const ANA = { phone: '+381600000201', shown: '+381 60 000 0201' };
const JELENA = { phone: '+381600000102', shown: '+381 60 000 0102' };
const ADMIN = { phone: '+381600000999', shown: '+381 60 000 0999' };

function track(page: Page, fragment: string) {
  const bodies: string[] = [];
  page.on('request', (r: Request) => {
    if (r.url().includes(fragment) && r.method() === 'POST') bodies.push(r.postData() ?? '');
  });
  return bodies;
}

async function mock(page: Page, pattern: string, status: number, body: object, delayMs = 0) {
  await page.route(pattern, async (route) => {
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

const expired = { code: 403, error_code: 'otp_expired', msg: 'Token has expired or is invalid' };

const field = (page: Page) => page.getByRole('textbox', { name: 'Code' });
const signIn = (page: Page) => page.getByRole('button', { name: 'Sign in' });
const resend = (page: Page) => page.getByRole('button', { name: /^Send a new code/ });
const change = (page: Page) => page.getByRole('button', { name: 'Change number' });

async function openCode(page: Page, phone = ANA.phone) {
  await page.goto(`/code?phone=${encodeURIComponent(phone)}`);
  await expect(page.getByRole('heading', { name: 'Enter the code' })).toBeVisible();
}

test.describe('US-0008 sign in with the SMS code', () => {
  test('AC1 code screen shows title, number, code field and buttons', async ({ page }) => {
    await openCode(page);
    await expect(page.getByText(`Sent to ${ANA.shown}`)).toBeVisible();
    await expect(field(page)).toHaveAttribute('autocomplete', 'one-time-code');
    await expect(signIn(page)).toBeDisabled();
    await expect(resend(page)).toBeEnabled();
    await expect(change(page)).toBeEnabled();
  });

  for (const user of [
    { name: 'Ana (client)', ...ANA },
    { name: 'Jelena (worker)', ...JELENA },
    { name: 'Admin', ...ADMIN },
  ]) {
    test(`AC1 real local Supabase signs ${user.name} in with 123456 and shows home`, async ({ page }) => {
      await openCode(page, user.phone);
      await field(page).fill('123456');
      await signIn(page).click();
      await expect(page).toHaveURL(/\/home$/);
      await expect(page.getByRole('heading', { name: 'Welcome' })).toBeVisible();
      await expect(page.getByText(`Signed in as ${user.shown}`)).toBeVisible();
    });
  }

  test('AC1 full flow from sign-in screen with real Supabase reaches home', async ({ page }) => {
    await mock(page, OTP, 200, {});
    await page.goto('/');
    await page.getByRole('textbox', { name: 'Phone number' }).fill('060 000 0201');
    await page.getByRole('button', { name: 'Send code' }).click();
    await expect(page.getByRole('heading', { name: 'Enter the code' })).toBeVisible();
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
  });

  test('AC2 a real wrong code 000000 shows the message, clears the field, and the right code then works', async ({ page }) => {
    await openCode(page);
    await field(page).fill('000000');
    await signIn(page).click();
    await expect(page.getByRole('alert')).toHaveText(WRONG);
    await expect(page).toHaveURL(/\/code/);
    await expect(field(page)).toHaveValue('');
    await expect(signIn(page)).toBeDisabled();
    await field(page).fill('123456');
    await expect(page.getByRole('alert')).toHaveCount(0);
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
  });

  for (const [name, status, body] of [
    ['otp_expired 403', 403, expired],
    ['invalid 400', 400, { code: 400, error_code: 'validation_failed', msg: 'Token has expired or is invalid' }],
    ['422', 422, { code: 422, msg: 'invalid' }],
  ] as const) {
    test(`AC2 mocked ${name} shows the wrong-or-expired message and the field is cleared`, async ({ page }) => {
      await mock(page, VERIFY, status, body);
      await openCode(page);
      await field(page).fill('123456');
      await signIn(page).click();
      await expect(page.getByRole('alert')).toHaveText(WRONG);
      await expect(field(page)).toHaveValue('');
      await expect(page).toHaveURL(/\/code/);
    });
  }

  test('AC2 retry after a mocked failure succeeds against the real Supabase', async ({ page }) => {
    await mock(page, VERIFY, 403, expired);
    await openCode(page);
    await field(page).fill('111111');
    await signIn(page).click();
    await expect(page.getByRole('alert')).toHaveText(WRONG);
    await page.unroute(VERIFY);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
  });

  test('AC2 typing again clears the error message', async ({ page }) => {
    await mock(page, VERIFY, 403, expired);
    await openCode(page);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByRole('alert')).toHaveText(WRONG);
    await field(page).pressSequentially('1');
    await expect(page.getByRole('alert')).toHaveCount(0);
  });

  test('AC2 rate limit and server errors on verify show their own messages', async ({ page }) => {
    await mock(page, VERIFY, 429, { code: 429, error_code: 'over_request_rate_limit', msg: 'slow down' });
    await openCode(page);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByRole('alert')).toHaveText(RATE);
    await page.unroute(VERIFY);
    await mock(page, VERIFY, 500, { code: 500, msg: 'boom' });
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByRole('alert')).toHaveText(VERIFY_FAILED);
    await expect(page).toHaveURL(/\/code/);
  });

  test('AC2 a network failure on verify shows the generic message', async ({ page }) => {
    await page.route(VERIFY, (route) => (route.request().method() === 'OPTIONS' ? route.fallback() : route.abort('failed')));
    await openCode(page);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByRole('alert')).toHaveText(VERIFY_FAILED);
    await expect(field(page)).toHaveValue('');
  });

  test('AC3 fewer than 6 digits keeps Sign in disabled and sends no request', async ({ page }) => {
    const verifies = track(page, '/auth/v1/verify');
    await openCode(page);
    for (const partial of ['', '1', '12345']) {
      await field(page).fill(partial);
      await expect(signIn(page)).toBeDisabled();
    }
    await field(page).fill('12345');
    await signIn(page).click({ force: true });
    await page.waitForTimeout(500);
    expect(verifies).toHaveLength(0);
    await field(page).fill('123456');
    await expect(signIn(page)).toBeEnabled();
  });

  test('AC3 letters and symbols are ignored', async ({ page }) => {
    await openCode(page);
    await field(page).fill('ab-1c2.3 ');
    await expect(field(page)).toHaveValue('123');
    await expect(signIn(page)).toBeDisabled();
  });

  test('AC3 pasted "123 456" is accepted as 123456', async ({ page }) => {
    const verifies = track(page, '/auth/v1/verify');
    await openCode(page);
    await field(page).fill('123 456');
    await expect(field(page)).toHaveValue('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
    expect(JSON.parse(verifies[0]).token).toBe('123456');
  });

  test('AC3 more than 6 digits are capped at 6', async ({ page }) => {
    await openCode(page);
    await field(page).fill('123456789012');
    await expect(field(page)).toHaveValue('123456');
    await field(page).fill('');
    await field(page).pressSequentially('12345678');
    await expect(field(page)).toHaveValue('123456');
  });

  test('AC4 Send a new code requests an OTP for the same number and starts a visible 60 s countdown', async ({ page }) => {
    await mock(page, OTP, 200, {});
    const otps = track(page, '/auth/v1/otp');
    await page.clock.install();
    await openCode(page);
    await resend(page).click();
    await expect(page.getByRole('button', { name: 'Send a new code in 1:00' })).toBeDisabled();
    expect(otps).toHaveLength(1);
    expect(JSON.parse(otps[0]).phone).toBe(ANA.phone);
    await page.clock.runFor(15_000);
    await expect(page.getByRole('button', { name: 'Send a new code in 0:45' })).toBeDisabled();
    await page.clock.runFor(44_000);
    await expect(page.getByRole('button', { name: 'Send a new code in 0:01' })).toBeDisabled();
    await page.clock.runFor(1_000);
    await expect(page.getByRole('button', { name: 'Send a new code', exact: true })).toBeEnabled();
    await page.getByRole('button', { name: 'Send a new code', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Send a new code in 1:00' })).toBeDisabled();
    expect(otps).toHaveLength(2);
  });

  test('AC4 clicking the disabled resend link does not request again', async ({ page }) => {
    await mock(page, OTP, 200, {});
    const otps = track(page, '/auth/v1/otp');
    await openCode(page);
    await resend(page).click();
    await expect(page.getByRole('button', { name: /Send a new code in/ })).toBeDisabled();
    await resend(page).click({ force: true });
    await page.waitForTimeout(300);
    expect(otps).toHaveLength(1);
  });

  test('AC4 resend rate limit shows the rate-limit message and leaves the link enabled', async ({ page }) => {
    await mock(page, OTP, 429, { code: 429, error_code: 'over_sms_send_rate_limit', msg: 'wait' });
    await openCode(page);
    await resend(page).click();
    await expect(page.getByRole('alert')).toHaveText(RATE);
    await expect(page.getByRole('button', { name: 'Send a new code', exact: true })).toBeEnabled();
  });

  test('AC4 resend server error shows the generic message, link stays enabled, retry works', async ({ page }) => {
    await mock(page, OTP, 500, { code: 500, msg: 'boom' });
    await openCode(page);
    await resend(page).click();
    await expect(page.getByRole('alert')).toHaveText(SEND_FAILED);
    await expect(page.getByRole('button', { name: 'Send a new code', exact: true })).toBeEnabled();
    await page.unroute(OTP);
    await mock(page, OTP, 200, {});
    await resend(page).click();
    await expect(page.getByRole('button', { name: /Send a new code in 1:00/ })).toBeDisabled();
    await expect(page.getByRole('alert')).toHaveCount(0);
  });

  test('AC4 a network failure on resend shows the generic message', async ({ page }) => {
    await page.route(OTP, (route) => (route.request().method() === 'OPTIONS' ? route.fallback() : route.abort('failed')));
    await openCode(page);
    await resend(page).click();
    await expect(page.getByRole('alert')).toHaveText(SEND_FAILED);
    await expect(page.getByRole('button', { name: 'Send a new code', exact: true })).toBeEnabled();
  });

  test('AC4 the countdown does not block signing in', async ({ page }) => {
    await mock(page, OTP, 200, {});
    await openCode(page);
    await resend(page).click();
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
  });

  test('AC5 Change number returns to sign-in with the number prefilled', async ({ page }) => {
    await openCode(page);
    await change(page).click();
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Phone number' })).toHaveValue(ANA.phone);
  });

  test('AC5 after Change number a different number can be used end to end', async ({ page }) => {
    await mock(page, OTP, 200, {});
    await openCode(page);
    await change(page).click();
    const input = page.getByRole('textbox', { name: 'Phone number' });
    await input.fill('060 000 0102');
    await page.getByRole('button', { name: 'Send code' }).click();
    await expect(page.getByText(`Sent to ${JELENA.shown}`)).toBeVisible();
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${JELENA.shown}`)).toBeVisible();
  });

  test('double click on Sign in sends one verify request', async ({ page }) => {
    await mock(page, VERIFY, 403, expired, 800);
    const verifies = track(page, '/auth/v1/verify');
    await openCode(page);
    await field(page).fill('123456');
    await signIn(page).dblclick();
    await expect(page.getByRole('alert')).toHaveText(WRONG);
    expect(verifies).toHaveLength(1);
  });

  test('while verifying the field is read-only and Change number is disabled', async ({ page }) => {
    await mock(page, VERIFY, 403, expired, 1000);
    await openCode(page);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(field(page)).toHaveAttribute('readonly', '');
    await expect(change(page)).toBeDisabled();
    await expect(page.getByRole('alert')).toHaveText(WRONG);
    await expect(change(page)).toBeEnabled();
  });

  test('pressing Enter in the code field submits the code', async ({ page }) => {
    const verifies = track(page, '/auth/v1/verify');
    await openCode(page);
    await field(page).fill('123456');
    await page.keyboard.press('Enter');
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
    expect(verifies).toHaveLength(1);
  });

  test('pressing Enter with an incomplete code sends nothing', async ({ page }) => {
    const verifies = track(page, '/auth/v1/verify');
    await openCode(page);
    await field(page).fill('123');
    await page.keyboard.press('Enter');
    await page.waitForTimeout(500);
    expect(verifies).toHaveLength(0);
    await expect(page).toHaveURL(/\/code/);
  });

  test('keyboard flow: field is focusable, Tab reaches Sign in, resend and Change number in order', async ({ page }) => {
    await openCode(page);
    await field(page).focus();
    await field(page).fill('123456');
    await page.keyboard.press('Tab');
    await expect(signIn(page)).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(resend(page)).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(change(page)).toBeFocused();
  });

  test('/code without a phone redirects to the sign-in screen (BUG-0024 fixed)', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto('/code');
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Phone number' })).toHaveValue('+381');
    expect(errors).toEqual([]);
  });

  for (const bad of ['abc', '+44 7911 123456', '%3Cb%3Ex%3C%2Fb%3E']) {
    test(`/code with an invalid phone "${bad}" redirects to sign-in`, async ({ page }) => {
      await page.goto(`/code?phone=${bad.startsWith('%') ? bad : encodeURIComponent(bad)}`);
      await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    });
  }

  test('/home without a session redirects to sign-in', async ({ page }) => {
    await page.goto('/home');
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Welcome' })).toHaveCount(0);
  });

  test('reload on /home keeps the session (web persists it)', async ({ page }) => {
    await openCode(page);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
    await page.reload();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
  });

  test('reload on /code keeps the number and the screen', async ({ page }) => {
    await openCode(page);
    await page.reload();
    await expect(page.getByText(`Sent to ${ANA.shown}`)).toBeVisible();
  });

  test('browser back from home after sign-in does not return to a working code screen with a stale code', async ({ page }) => {
    await page.goto('/');
    await mock(page, OTP, 200, {});
    await page.getByRole('textbox', { name: 'Phone number' }).fill('0600000201');
    await page.getByRole('button', { name: 'Send code' }).click();
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
    await page.goBack();
    // Observation only: the code screen is replaced by home, so back lands on sign-in; the code field is never prefilled.
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  });

  test('no token or code appears in the URL after sign-in', async ({ page }) => {
    await openCode(page);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
    const url = page.url();
    expect(url).not.toMatch(/token|access_token|123456|381600000201/i);
  });

  test('no console errors on the happy path', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (e) => errors.push(e.message));
    page.on('console', (m) => {
      if (m.type() === 'error') errors.push(m.text());
    });
    await openCode(page);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
    expect(errors).toEqual([]);
  });

  test('wrong-code message is the only alert and is announced as role alert', async ({ page }) => {
    await mock(page, VERIFY, 403, expired);
    await openCode(page);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByRole('alert')).toHaveCount(1);
  });

  test('360px: code screen and home do not overflow horizontally', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 640 });
    await openCode(page);
    await expect(change(page)).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await field(page).fill('123456');
    await signIn(page).click();
    await expect(page.getByText(`Signed in as ${ANA.shown}`)).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('desktop 1920px: code screen content stays within the viewport and buttons are reachable', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await openCode(page);
    for (const b of [signIn(page), resend(page), change(page)]) {
      await expect(b).toBeVisible();
      const box = await b.boundingBox();
      expect(box!.x + box!.width).toBeLessThanOrEqual(1920);
      expect(box!.height).toBeGreaterThanOrEqual(44);
    }
  });

  test('code field and buttons stay usable at 200% zoom (640px effective width)', async ({ page }) => {
    await page.setViewportSize({ width: 640, height: 400 });
    await openCode(page);
    await page.evaluate(() => {
      document.documentElement.style.fontSize = '200%';
    });
    await field(page).fill('123456');
    await expect(signIn(page)).toBeEnabled();
    await signIn(page).scrollIntoViewIfNeeded();
    await expect(change(page)).toBeVisible();
  });
});
