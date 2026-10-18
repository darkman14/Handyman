import { createClient } from '@supabase/supabase-js';
import { sessionStorage } from './session-storage';

const url = process.env.EXPO_PUBLIC_SUPABASE_URL ?? '';
const anonKey = process.env.EXPO_PUBLIC_SUPABASE_ANON_KEY ?? '';

// Supabase is used for sign-in only (OTP). Business data goes through the API.
export const supabase = createClient(url, anonKey, {
  auth: {
    storage: sessionStorage,
    autoRefreshToken: true,
    persistSession: true,
    detectSessionInUrl: false,
  },
});
