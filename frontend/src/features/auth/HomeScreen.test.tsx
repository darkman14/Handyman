import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { supabase } from '@/lib/supabase';
import { AuthProvider } from './AuthProvider';
import { HomeScreen } from './HomeScreen';

const getSession = supabase.auth.getSession as jest.Mock;
const signOut = supabase.auth.signOut as jest.Mock;

const renderHome = () => render(<AuthProvider><HomeScreen /></AuthProvider>);

beforeEach(() => {
  jest.clearAllMocks();
  getSession.mockResolvedValue({ data: { session: { user: { phone: '381600000201' } } }, error: null });
});

it('greets the signed-in user by phone', async () => {
  await renderHome();
  expect(await screen.findByText('Signed in as +381 60 000 0201')).toBeTruthy();
});

it('asks for confirmation before signing out and Cancel keeps the session', async () => {
  await renderHome();
  await fireEvent.press(await screen.findByRole('button', { name: 'Sign out' }));
  expect(screen.getByText('Sign out?')).toBeTruthy();
  await fireEvent.press(screen.getByRole('button', { name: 'Cancel' }));
  expect(screen.queryByText('Sign out?')).toBeNull();
  expect(signOut).not.toHaveBeenCalled();
});

it('AC4 confirming clears this device session', async () => {
  await renderHome();
  await fireEvent.press(await screen.findByRole('button', { name: 'Sign out' }));
  const buttons = screen.getAllByRole('button', { name: 'Sign out' });
  await fireEvent.press(buttons[buttons.length - 1]);
  await waitFor(() => expect(signOut).toHaveBeenCalledWith({ scope: 'local' }));
  await waitFor(() => expect(screen.queryByText('Sign out?')).toBeNull());
});
