import { useCallback, useRef, useState } from 'react';
import { supabase } from '@/lib/supabase';

export type VerifyCodeError = 'invalid' | 'rateLimited' | 'failed';

function mapError(error: { status?: number; code?: string }): VerifyCodeError {
  const code = error.code ?? '';
  if (error.status === 429 || code.includes('rate_limit')) return 'rateLimited';
  if (code === 'otp_expired' || [400, 401, 403, 422].includes(error.status ?? 0)) return 'invalid';
  return 'failed';
}

export function useVerifyCode() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<VerifyCodeError | null>(null);
  const inFlight = useRef(false);

  const verify = useCallback(async (phone: string, token: string): Promise<boolean> => {
    if (inFlight.current) return false;
    inFlight.current = true;
    setLoading(true);
    setError(null);
    try {
      const { error: err } = await supabase.auth.verifyOtp({ phone, token, type: 'sms' });
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

  return { verify, loading, error, clearError: () => setError(null) };
}
