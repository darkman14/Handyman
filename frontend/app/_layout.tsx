import { Stack } from 'expo-router';
import { AuthGate } from '@/features/auth/AuthGate';
import { AuthProvider } from '@/features/auth/AuthProvider';

export default function RootLayout() {
  return (
    <AuthProvider>
      <AuthGate>
        <Stack />
      </AuthGate>
    </AuthProvider>
  );
}
