import 'react-native-get-random-values';
import AsyncStorage from '@react-native-async-storage/async-storage';
import * as aesjs from 'aes-js';
import * as SecureStore from 'expo-secure-store';

// Supabase sessions exceed SecureStore's size limit, so the session is encrypted
// in AsyncStorage and only the AES key lives in SecureStore.
const toHex = (b: Uint8Array) => aesjs.utils.hex.fromBytes(b);

async function getKey(name: string): Promise<Uint8Array> {
  const existing = await SecureStore.getItemAsync(name);
  if (existing) return aesjs.utils.hex.toBytes(existing);
  const key = crypto.getRandomValues(new Uint8Array(32));
  await SecureStore.setItemAsync(name, toHex(key));
  return key;
}

export const sessionStorage = {
  async getItem(name: string): Promise<string | null> {
    const encrypted = await AsyncStorage.getItem(name);
    if (!encrypted) return null;
    const key = await getKey(name);
    const cipher = new aesjs.ModeOfOperation.ctr(key, new aesjs.Counter(1));
    return aesjs.utils.utf8.fromBytes(cipher.decrypt(aesjs.utils.hex.toBytes(encrypted)));
  },
  async setItem(name: string, value: string): Promise<void> {
    const key = await getKey(name);
    const cipher = new aesjs.ModeOfOperation.ctr(key, new aesjs.Counter(1));
    await AsyncStorage.setItem(name, toHex(cipher.encrypt(aesjs.utils.utf8.toBytes(value))));
  },
  async removeItem(name: string): Promise<void> {
    await AsyncStorage.removeItem(name);
    await SecureStore.deleteItemAsync(name);
  },
};
