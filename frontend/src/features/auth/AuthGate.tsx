import { useGlobalSearchParams, usePathname, useRouter } from 'expo-router';
import { useEffect, type ReactNode } from 'react';
import { en } from '@/i18n/en';
import { LoadingScreen } from '@/ui';
import { useAuth } from './AuthProvider';
import { DEFAULT_HOME, isPublicPath, safeRedirect } from './redirect';

/**
 * Keeps children (the navigator) mounted and covers them with a loading screen while the
 * session is restored or a redirect is pending, so no protected or sign-in screen flashes.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  const pathname = usePathname();
  const params = useGlobalSearchParams<{ redirect?: string }>();
  const router = useRouter();

  const isPublic = isPublicPath(pathname);
  const redirectParam = typeof params.redirect === 'string' ? params.redirect : undefined;
  const target = status === 'signedOut' && !isPublic
    ? { pathname: '/' as const, params: pathname === DEFAULT_HOME ? {} : { redirect: pathname } }
    : status === 'signedIn' && isPublic
      ? safeRedirect(redirectParam) ?? DEFAULT_HOME
      : null;
  const key = target ? JSON.stringify(target) : null;

  useEffect(() => {
    if (target) router.replace(target as never);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  const blocked = status === 'restoring' || target !== null;
  return (
    <>
      {children}
      {blocked ? <LoadingScreen overlay label={en.session.restoring} /> : null}
    </>
  );
}
