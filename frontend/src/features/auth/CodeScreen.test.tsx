import { render, screen } from '@testing-library/react-native';
import { CodeScreen } from './CodeScreen';

jest.mock('expo-router', () => ({
  useRouter: () => ({ push: jest.fn(), back: jest.fn() }),
  useLocalSearchParams: () => ({ phone: '+381600000201' }),
}));

it('shows the number the code was sent to', async () => {
  await render(<CodeScreen />);
  expect(screen.getByText('+381600000201')).toBeTruthy();
});
