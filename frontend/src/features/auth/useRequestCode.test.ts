import { act, renderHook } from '@testing-library/react-native';
import { supabase } from '@/lib/supabase';
import { useRequestCode } from './useRequestCode';

const signIn = supabase.auth.signInWithOtp as jest.Mock;

it('requests only once when called twice while a request is in flight', async () => {
  let resolve!: (v: unknown) => void;
  signIn.mockReset().mockReturnValue(new Promise((r) => (resolve = r)));
  const { result } = await renderHook(() => useRequestCode());
  let results: boolean[] = [];
  await act(async () => {
    const calls = Promise.all([result.current.requestCode('+381600000201'), result.current.requestCode('+381600000201')]);
    resolve({ data: {}, error: null });
    results = await calls;
  });
  expect(signIn).toHaveBeenCalledTimes(1);
  expect(results.filter(Boolean)).toHaveLength(1);
});
