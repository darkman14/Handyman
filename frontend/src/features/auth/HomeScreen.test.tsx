import { render, screen, waitFor } from '@testing-library/react-native';
import { supabase } from '@/lib/supabase';
import { HomeScreen } from './HomeScreen';

const mockRedirect = jest.fn();
jest.mock('expo-router', () => ({
  Redirect: ({ href }: { href: string }) => {
    mockRedirect(href);
    return null;
  },
}));

const getSession = supabase.auth.getSession as jest.Mock;

beforeEach(() => jest.clearAllMocks());

it('greets the signed-in user by phone', async () => {
  getSession.mockResolvedValue({ data: { session: { user: { phone: '381600000201' } } }, error: null });
  await render(<HomeScreen />);
  expect(await screen.findByText('Signed in as +381 60 000 0201')).toBeTruthy();
});

it('sends a visitor without a session to sign-in', async () => {
  getSession.mockResolvedValue({ data: { session: null }, error: null });
  await render(<HomeScreen />);
  await waitFor(() => expect(mockRedirect).toHaveBeenCalledWith('/'));
});
