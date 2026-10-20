import AsyncStorage from '@react-native-async-storage/async-storage';
import * as SecureStore from 'expo-secure-store';

// Keychain and Keystore entries are capped near 2 KB and a Supabase session is larger, so it is
// stored in pieces. Device-only: never in a backup, never restored onto another phone.
const CHUNK_SIZE = 1800;
const options: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};

// SecureStore keys may only hold letters, digits, ".", "-" and "_".
const safe = (key: string) => key.replace(/[^A-Za-z0-9._-]/g, '_');
const countKey = (key: string) => `${safe(key)}.count`;
const partKey = (key: string, index: number) => `${safe(key)}.${index}`;

async function partCount(key: string): Promise<number> {
  const stored = Number(await SecureStore.getItemAsync(countKey(key), options));
  return Number.isInteger(stored) && stored > 0 ? stored : 0;
}

async function removeParts(key: string, from: number, to: number): Promise<void> {
  for (let index = from; index < to; index += 1) {
    await SecureStore.deleteItemAsync(partKey(key, index), options);
  }
}

/** Supabase auth storage backed by the Keychain / Keystore instead of plain AsyncStorage. */
export const secureSessionStorage = {
  async getItem(key: string): Promise<string | null> {
    const count = await partCount(key);
    if (count > 0) {
      const parts = await Promise.all(
        Array.from({ length: count }, (_, index) => SecureStore.getItemAsync(partKey(key, index), options)),
      );
      return parts.every((part) => part !== null) ? parts.join('') : null;
    }

    // Builds before this one kept the session in AsyncStorage; move it across once, then forget it.
    const legacy = await AsyncStorage.getItem(key);
    if (legacy !== null) {
      await secureSessionStorage.setItem(key, legacy);
      await AsyncStorage.removeItem(key);
    }
    return legacy;
  },

  async setItem(key: string, value: string): Promise<void> {
    const previous = await partCount(key);
    const parts = value.match(new RegExp(`[\\s\\S]{1,${CHUNK_SIZE}}`, 'g')) ?? [''];
    for (let index = 0; index < parts.length; index += 1) {
      await SecureStore.setItemAsync(partKey(key, index), parts[index], options);
    }
    await SecureStore.setItemAsync(countKey(key), String(parts.length), options);
    await removeParts(key, parts.length, previous);
  },

  async removeItem(key: string): Promise<void> {
    await removeParts(key, 0, await partCount(key));
    await SecureStore.deleteItemAsync(countKey(key), options);
    await AsyncStorage.removeItem(key);
  },
};
