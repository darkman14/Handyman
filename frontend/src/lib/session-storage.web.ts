// Web uses supabase-js's default browser storage (localStorage), protected by a strict CSP from Caddy.
// Returning undefined lets the Supabase client fall back to its default.
export const sessionStorage = undefined;
