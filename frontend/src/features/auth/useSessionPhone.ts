import { useEffect, useState } from 'react';
import { supabase } from '@/lib/supabase';

/** Phone of the signed-in user: undefined while loading, null when there is no session. */
export function useSessionPhone() {
  const [phone, setPhone] = useState<string | null | undefined>(undefined);
  useEffect(() => {
    let active = true;
    supabase.auth
      .getSession()
      .then(({ data }) => active && setPhone(data.session?.user?.phone ?? null))
      .catch(() => active && setPhone(null));
    return () => {
      active = false;
    };
  }, []);
  return phone;
}
