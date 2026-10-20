import AsyncStorage from '@react-native-async-storage/async-storage';
import { secureSessionStorage } from './secureSessionStorage';

const mockSecure = new Map<string, string>();
const mockPlain = new Map<string, string>();

jest.mock('expo-secure-store', () => ({
  AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY: 'afterFirstUnlockThisDeviceOnly',
  getItemAsync: jest.fn(async (key: string) => mockSecure.get(key) ?? null),
  setItemAsync: jest.fn(async (key: string, value: string) => void mockSecure.set(key, value)),
  deleteItemAsync: jest.fn(async (key: string) => void mockSecure.delete(key)),
}));
jest.mock('@react-native-async-storage/async-storage', () => ({
  getItem: jest.fn(async (key: string) => mockPlain.get(key) ?? null),
  removeItem: jest.fn(async (key: string) => void mockPlain.delete(key)),
}));

const KEY = 'sb-zafexeixnmguccancpvw-auth-token';
const session = JSON.stringify({ access_token: 'a'.repeat(3000), refresh_token: 'r'.repeat(40) });

beforeEach(() => {
  mockSecure.clear();
  mockPlain.clear();
});

describe('secureSessionStorage', () => {
  it('splits a session too big for one keychain entry and reads it back whole', async () => {
    await secureSessionStorage.setItem(KEY, session);

    expect([...mockSecure.values()].every((value) => value.length <= 1800)).toBe(true);
    expect(await secureSessionStorage.getItem(KEY)).toBe(session);
  });

  it('moves a session an older build left in plain storage, then deletes the plain copy', async () => {
    mockPlain.set(KEY, session);

    expect(await secureSessionStorage.getItem(KEY)).toBe(session);
    expect(mockPlain.has(KEY)).toBe(false);
    expect(await secureSessionStorage.getItem(KEY)).toBe(session);
  });

  it('leaves no stale pieces behind when a shorter session replaces a longer one', async () => {
    await secureSessionStorage.setItem(KEY, session);
    await secureSessionStorage.setItem(KEY, 'short');

    expect(await secureSessionStorage.getItem(KEY)).toBe('short');
    expect(mockSecure.size).toBe(2);
  });

  it('removes every piece and any plain copy', async () => {
    await secureSessionStorage.setItem(KEY, session);
    mockPlain.set(KEY, 'old');

    await secureSessionStorage.removeItem(KEY);

    expect(mockSecure.size).toBe(0);
    expect(AsyncStorage.removeItem).toHaveBeenCalledWith(KEY);
    expect(await secureSessionStorage.getItem(KEY)).toBeNull();
  });
});
