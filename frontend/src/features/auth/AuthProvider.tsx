import type { Session } from '@supabase/supabase-js';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { supabase } from '@/lib/supabase';

export type AuthStatus = 'restoring' | 'signedIn' | 'signedOut';
type AuthValue = { status: AuthStatus; session: Session | null; signOut: () => Promise<void> };

const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<{ status: AuthStatus; session: Session | null }>({
    status: 'restoring',
    session: null,
  });

  useEffect(() => {
    let active = true;
    const apply = (session: Session | null) =>
      active && setState({ status: session ? 'signedIn' : 'signedOut', session });

    // Restores the stored session; a failed refresh (revoked, expired) ends as signed out.
    supabase.auth
      .getSession()
      .then(({ data, error }) => apply(error ? null : data.session))
      .catch(() => apply(null));

    // Also fires when another tab signs in or out, or when a refresh fails later.
    const { data } = supabase.auth.onAuthStateChange((_event, session) => apply(session));
    return () => {
      active = false;
      data.subscription.unsubscribe();
    };
  }, []);

  const signOut = useCallback(async () => {
    try {
      // Local scope: only this device. Supabase clears stored session even if the call fails.
      await supabase.auth.signOut({ scope: 'local' });
    } catch {
      // Fall through: the device must end up signed out either way.
    }
    setState({ status: 'signedOut', session: null });
  }, []);

  const value = useMemo(() => ({ ...state, signOut }), [state, signOut]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
