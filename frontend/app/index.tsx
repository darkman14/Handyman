import { SignInScreen } from '@/features/auth/SignInScreen';

// Signed-out landing. Session-aware redirects arrive with US-0008/US-0009.
export default function Index() {
  return <SignInScreen />;
}
