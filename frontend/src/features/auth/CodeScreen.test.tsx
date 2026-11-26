import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { en } from '@/i18n/en';
import { supabase } from '@/lib/supabase';
import { CodeScreen } from './CodeScreen';

const mockReplace = jest.fn();
const mockRedirect = jest.fn();
let mockParams: { phone?: string; redirect?: string } = {};
jest.mock('expo-router', () => ({
  useRouter: () => ({ push: jest.fn(), back: jest.fn(), replace: mockReplace }),
  useLocalSearchParams: () => mockParams,
  Redirect: ({ href }: { href: string }) => {
    mockRedirect(href);
    return null;
  },
}));

const verifyOtp = supabase.auth.verifyOtp as jest.Mock;
const signInWithOtp = supabase.auth.signInWithOtp as jest.Mock;
const field = () => screen.getByLabelText(en.auth.codeLabel);
const signInButton = () => screen.getByRole('button', { name: 'Sign in' });
const resend = () => screen.getByRole('button', { name: /Send a new code/ });

beforeEach(() => {
  jest.clearAllMocks();
  mockParams = { phone: '+381600000201' };
  verifyOtp.mockResolvedValue({ data: {}, error: null });
  signInWithOtp.mockResolvedValue({ data: {}, error: null });
});
afterEach(() => jest.useRealTimers());

describe('CodeScreen', () => {
  it('shows the title and the formatted number', async () => {
    await render(<CodeScreen />);
    expect(screen.getByRole('header')).toHaveTextContent('Enter the code');
    expect(screen.getByText('Sent to +381 60 000 0201')).toBeTruthy();
    expect(field().props.autoComplete).toBe('one-time-code');
    expect(field().props.textContentType).toBe('oneTimeCode');
    expect(field().props.keyboardType).toBe('number-pad');
  });

  it('AC1 verifies the code with Supabase and goes home', async () => {
    await render(<CodeScreen />);
    await fireEvent.changeText(field(), '123456');
    await fireEvent.press(signInButton());
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/home'));
    expect(verifyOtp).toHaveBeenCalledWith({ phone: '+381600000201', token: '123456', type: 'sms' });
  });

  it('AC2 keeps the user here, shows the error, clears the field and allows a retry', async () => {
    verifyOtp.mockResolvedValueOnce({ data: {}, error: { status: 403, code: 'otp_expired' } });
    await render(<CodeScreen />);
    await fireEvent.changeText(field(), '000000');
    await fireEvent.press(signInButton());
    expect(await screen.findByRole('alert')).toHaveTextContent('That code is wrong or has expired.');
    expect(field().props.value).toBe('');
    expect(mockReplace).not.toHaveBeenCalled();
    expect(signInButton()).toBeDisabled();
    await fireEvent.changeText(field(), '123456');
    expect(screen.queryByRole('alert')).toBeNull();
    await fireEvent.press(signInButton());
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/home'));
    expect(verifyOtp).toHaveBeenCalledTimes(2);
  });

  it('AC3 disables Sign in until 6 digits and sends no request', async () => {
    await render(<CodeScreen />);
    expect(signInButton()).toBeDisabled();
    await fireEvent.changeText(field(), '12345');
    expect(signInButton()).toBeDisabled();
    await fireEvent.press(signInButton());
    expect(verifyOtp).not.toHaveBeenCalled();
    await fireEvent.changeText(field(), '123456');
    expect(signInButton()).toBeEnabled();
  });

  it.each([
    ['123 456', '123456'],
    ['12a3-4b56', '123456'],
    ['1234567890', '123456'],
  ])('accepts pasted %p as %p', async (pasted, expected) => {
    await render(<CodeScreen />);
    await fireEvent.changeText(field(), pasted);
    expect(field().props.value).toBe(expected);
    await fireEvent.press(signInButton());
    await waitFor(() => expect(verifyOtp).toHaveBeenCalledWith(expect.objectContaining({ token: expected })));
  });

  it('shows loading while verifying and sends once on a double tap', async () => {
    let resolve!: (v: unknown) => void;
    verifyOtp.mockReturnValue(new Promise((r) => (resolve = r)));
    await render(<CodeScreen />);
    await fireEvent.changeText(field(), '123456');
    const tap = fireEvent.press(signInButton());
    await waitFor(() => expect(signInButton()).toBeBusy());
    expect(signInButton()).toBeDisabled();
    expect(field().props.editable).toBe(false);
    await fireEvent.press(signInButton());
    resolve({ data: {}, error: null });
    await tap;
    await waitFor(() => expect(mockReplace).toHaveBeenCalledTimes(1));
    expect(verifyOtp).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['rate limit', { status: 429, code: 'over_request_rate_limit' }, 'Too many attempts. Try again in a minute.'],
    ['server error', { status: 500, code: 'unexpected_failure' }, "Couldn't sign you in. Try again."],
  ])('shows a readable message for %s', async (_n, err, message) => {
    verifyOtp.mockResolvedValueOnce({ data: {}, error: err });
    await render(<CodeScreen />);
    await fireEvent.changeText(field(), '123456');
    await fireEvent.press(signInButton());
    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('shows the generic message on a network failure', async () => {
    verifyOtp.mockRejectedValueOnce(new Error('Network request failed'));
    await render(<CodeScreen />);
    await fireEvent.changeText(field(), '123456');
    await fireEvent.press(signInButton());
    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't sign you in. Try again.");
  });

  it('AC4 requests a new code for the same number and counts down 60 seconds', async () => {
    jest.useFakeTimers();
    await render(<CodeScreen />);
    expect(resend()).toBeEnabled();
    await fireEvent.press(resend());
    expect(signInWithOtp).toHaveBeenCalledWith({ phone: '+381600000201' });
    expect(screen.getByRole('button', { name: 'Send a new code in 1:00' })).toBeDisabled();
    await act(async () => {
      jest.advanceTimersByTime(18000);
    });
    expect(screen.getByRole('button', { name: 'Send a new code in 0:42' })).toBeDisabled();
    await fireEvent.press(resend());
    expect(signInWithOtp).toHaveBeenCalledTimes(1);
    await act(async () => {
      jest.advanceTimersByTime(42000);
    });
    expect(screen.getByRole('button', { name: 'Send a new code' })).toBeEnabled();
  });

  it('AC4 shows a Supabase error from resend and does not start the cooldown', async () => {
    signInWithOtp.mockResolvedValueOnce({ data: {}, error: { status: 429, code: 'over_sms_send_rate_limit' } });
    await render(<CodeScreen />);
    await fireEvent.press(resend());
    expect(await screen.findByRole('alert')).toHaveTextContent('Too many attempts. Try again in a minute.');
    expect(screen.getByRole('button', { name: 'Send a new code' })).toBeEnabled();
  });

  it('AC5 goes back to sign-in with the number kept', async () => {
    await render(<CodeScreen />);
    await fireEvent.press(screen.getByRole('button', { name: 'Change number' }));
    expect(mockReplace).toHaveBeenCalledWith({ pathname: '/', params: { phone: '+381600000201' } });
  });

  it.each([[undefined], ['abc'], ['+44 7700 900123']])(
    'BUG-0024 redirects to sign-in when the phone is %p',
    async (phone) => {
      mockParams = { phone };
      await render(<CodeScreen />);
      expect(mockRedirect).toHaveBeenCalledWith('/');
      expect(screen.queryByLabelText(en.auth.codeLabel)).toBeNull();
    },
  );

  it('US-0009 goes to the originally requested screen after signing in', async () => {
    mockParams = { phone: '+381600000201', redirect: '/jobs/42' };
    await render(<CodeScreen />);
    await fireEvent.changeText(field(), '123456');
    await fireEvent.press(signInButton());
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/jobs/42'));
  });

  it('US-0009 ignores an external redirect target and goes home', async () => {
    mockParams = { phone: '+381600000201', redirect: 'https://evil.com' };
    await render(<CodeScreen />);
    await fireEvent.changeText(field(), '123456');
    await fireEvent.press(signInButton());
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/home'));
  });
});
