import { useCallback, useRef, useState } from 'react';
import { supabase } from '@/lib/supabase';

export type RequestCodeError = 'rateLimited' | 'failed';

function mapError(error: { status?: number; code?: string }): RequestCodeError {
  if (error.status === 429 || (error.code ?? '').includes('rate_limit')) return 'rateLimited';
  return 'failed';
}

export function useRequestCode() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<RequestCodeError | null>(null);
  const inFlight = useRef(false);

  const requestCode = useCallback(async (phone: string): Promise<boolean> => {
    if (inFlight.current) return false;
    inFlight.current = true;
    setLoading(true);
    setError(null);
    try {
      const { error: err } = await supabase.auth.signInWithOtp({ phone });
      if (err) {
        setError(mapError(err));
        return false;
      }
      return true;
    } catch {
      setError('failed');
      return false;
    } finally {
      inFlight.current = false;
      setLoading(false);
    }
  }, []);

  return { requestCode, loading, error };
}
