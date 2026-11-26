import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { en } from '@/i18n/en';
import { supabase } from '@/lib/supabase';
import { SignInScreen } from './SignInScreen';

const mockPush = jest.fn();
jest.mock('expo-router', () => ({
  useRouter: () => ({ push: mockPush, back: jest.fn() }),
  useLocalSearchParams: () => ({}),
}));

const signIn = supabase.auth.signInWithOtp as jest.Mock;
const field = () => screen.getByLabelText(en.auth.phoneLabel);
const send = () => screen.getByRole('button', { name: en.auth.sendCode });

beforeEach(() => {
  jest.clearAllMocks();
  signIn.mockResolvedValue({ data: {}, error: null });
});

describe('SignInScreen', () => {
  it('AC1 shows title, helper, prefilled phone field and send button', async () => {
    await render(<SignInScreen />);
    expect(screen.getByText(en.auth.signInTitle)).toBeTruthy();
    expect(screen.getByText(en.auth.signInHelp)).toBeTruthy();
    expect(field().props.value).toBe('+381');
    expect(field().props.keyboardType).toBe('phone-pad');
    expect(send()).toBeTruthy();
  });

  it.each([['060 000 0201'], ['+381 60 000 0201'], ['+381 060 000 0201']])(
    'AC2 requests a normalized code for %s and goes to the code screen',
    async (input) => {
      await render(<SignInScreen />);
      await fireEvent.changeText(field(), input);
      await fireEvent.press(send());
      await waitFor(() => expect(mockPush).toHaveBeenCalled());
      expect(signIn).toHaveBeenCalledWith({ phone: '+381600000201' });
      expect(mockPush).toHaveBeenCalledWith({ pathname: '/code', params: { phone: '+381600000201' } });
    },
  );

  it('AC2 shows loading with the field and button disabled while sending', async () => {
    let resolve!: (v: unknown) => void;
    signIn.mockReturnValue(new Promise((r) => (resolve = r)));
    await render(<SignInScreen />);
    await fireEvent.changeText(field(), '0600000201');
    // The handler stays pending until Supabase answers, so await the tap only after resolving.
    const tap = fireEvent.press(send());
    await waitFor(() => expect(send()).toBeBusy());
    expect(send()).toBeDisabled();
    expect(field().props.editable).toBe(false);
    resolve({ data: {}, error: null });
    await tap;
    await waitFor(() => expect(mockPush).toHaveBeenCalledTimes(1));
    expect(signIn).toHaveBeenCalledTimes(1);
  });

  it.each([[''], ['+381'], ['+44 7700 900123'], ['123'], ['0'.repeat(80)]])(
    'AC3 rejects %p without calling Supabase',
    async (input) => {
      await render(<SignInScreen />);
      await fireEvent.changeText(field(), input);
      await fireEvent.press(send());
      expect(await screen.findByText(en.auth.invalidPhone)).toBeTruthy();
      expect(screen.getByRole('alert')).toHaveTextContent(en.auth.invalidPhone);
      expect(field().props['aria-invalid']).toBe(true);
      expect(field().props['aria-describedby']).toBe(screen.getByRole('alert').props.nativeID);
      expect(signIn).not.toHaveBeenCalled();
      expect(mockPush).not.toHaveBeenCalled();
    },
  );

  it('AC4 maps a rate limit error and allows retry', async () => {
    signIn.mockResolvedValueOnce({ data: {}, error: { status: 429, code: 'over_sms_send_rate_limit' } });
    await render(<SignInScreen />);
    await fireEvent.changeText(field(), '0600000201');
    await fireEvent.press(send());
    expect(await screen.findByText('Too many attempts. Try again in a minute.')).toBeTruthy();
    expect(mockPush).not.toHaveBeenCalled();
    await fireEvent.press(send());
    await waitFor(() => expect(mockPush).toHaveBeenCalledTimes(1));
    expect(screen.queryByText('Too many attempts. Try again in a minute.')).toBeNull();
  });

  it.each([
    ['other error', () => signIn.mockResolvedValueOnce({ data: {}, error: { status: 500, code: 'unexpected_failure' } })],
    ['network failure', () => signIn.mockRejectedValueOnce(new Error('Network request failed'))],
  ])('AC4 maps %s to the generic message', async (_name, arrange) => {
    arrange();
    await render(<SignInScreen />);
    await fireEvent.changeText(field(), '0600000201');
    await fireEvent.press(send());
    expect(await screen.findByText("Couldn't send the code. Try again.")).toBeTruthy();
    expect(mockPush).not.toHaveBeenCalled();
    expect(send()).toBeEnabled();
  });
});
