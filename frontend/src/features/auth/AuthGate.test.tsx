import { act, render, screen, waitFor } from '@testing-library/react-native';
import { Text } from 'react-native';
import { supabase } from '@/lib/supabase';
import { AuthGate } from './AuthGate';
import { AuthProvider } from './AuthProvider';

const mockReplace = jest.fn();
let mockPathname = '/home';
let mockParams: { redirect?: string } = {};
jest.mock('expo-router', () => ({
  useRouter: () => ({ replace: mockReplace }),
  usePathname: () => mockPathname,
  useGlobalSearchParams: () => mockParams,
}));

const getSession = supabase.auth.getSession as jest.Mock;
const onAuthStateChange = supabase.auth.onAuthStateChange as jest.Mock;
const session = { user: { phone: '381600000201' } };

const renderGate = () =>
  render(<AuthProvider><AuthGate><Text>app</Text></AuthGate></AuthProvider>);
const loading = () => screen.queryByRole('progressbar');

beforeEach(() => {
  jest.clearAllMocks();
  mockPathname = '/home';
  mockParams = {};
  getSession.mockResolvedValue({ data: { session: null }, error: null });
});

describe('AuthGate', () => {
  it('shows a loading screen and does not redirect while the session is restored', async () => {
    getSession.mockReturnValue(new Promise(() => {}));
    await renderGate();
    expect(loading()).toBeTruthy();
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('AC1 stays on the protected screen when a session is restored', async () => {
    getSession.mockResolvedValue({ data: { session }, error: null });
    await renderGate();
    await waitFor(() => expect(loading()).toBeNull());
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('AC2 sends a signed-out user from a protected screen to sign-in, remembering the screen', async () => {
    mockPathname = '/jobs/42';
    await renderGate();
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith({ pathname: '/', params: { redirect: '/jobs/42' } }));
  });

  it('AC2 does not remember the default home as a redirect', async () => {
    await renderGate();
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith({ pathname: '/', params: {} }));
  });

  it('AC2 sends a user who just signed in to the screen they asked for', async () => {
    mockPathname = '/code';
    mockParams = { redirect: '/jobs/42' };
    getSession.mockResolvedValue({ data: { session }, error: null });
    await renderGate();
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/jobs/42'));
  });

  it.each(['https://evil.com', '//evil.com', 'javascript:alert(1)'])(
    'AC2 ignores the unsafe redirect target %p',
    async (redirect) => {
      mockPathname = '/code';
      mockParams = { redirect };
      getSession.mockResolvedValue({ data: { session }, error: null });
      await renderGate();
      await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/home'));
    },
  );

  it.each(['/', '/code'])('AC3 sends a signed-in user from %p to home', async (path) => {
    mockPathname = path;
    getSession.mockResolvedValue({ data: { session }, error: null });
    await renderGate();
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/home'));
  });

  it('leaves a signed-out user on the sign-in screen', async () => {
    mockPathname = '/';
    await renderGate();
    await waitFor(() => expect(loading()).toBeNull());
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it.each([
    ['an error result', () => getSession.mockResolvedValue({ data: { session: null }, error: { message: 'Invalid Refresh Token' } })],
    ['a thrown error', () => getSession.mockRejectedValue(new Error('refresh failed'))],
  ])('AC5 ends on sign-in after a refresh failure (%s)', async (_n, arrange) => {
    arrange();
    await renderGate();
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith({ pathname: '/', params: {} }));
    expect(loading()).toBeTruthy(); // covered until the navigation lands, never a blank error screen
  });

  it('signs out when another tab signs out', async () => {
    getSession.mockResolvedValue({ data: { session }, error: null });
    await renderGate();
    await waitFor(() => expect(loading()).toBeNull());
    const listener = onAuthStateChange.mock.calls[0][0];
    await act(async () => listener('SIGNED_OUT', null));
    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith({ pathname: '/', params: {} }));
  });
});
