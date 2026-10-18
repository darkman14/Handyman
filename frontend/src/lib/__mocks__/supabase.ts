export const supabase = {
  auth: {
    signInWithOtp: jest.fn().mockResolvedValue({ data: {}, error: null }),
    verifyOtp: jest.fn().mockResolvedValue({ data: { session: null, user: null }, error: null }),
    getSession: jest.fn().mockResolvedValue({ data: { session: null }, error: null }),
    signOut: jest.fn().mockResolvedValue({ error: null }),
    onAuthStateChange: jest.fn().mockReturnValue({ data: { subscription: { unsubscribe: jest.fn() } } }),
  },
};
