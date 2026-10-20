import { Alert } from 'react-native';
import { countUnsyncedChanges } from '../db/localStore';

/** Resolves true when it is safe to sign out: nothing unsynced, or the person chose to lose it. */
export async function confirmSignOut(): Promise<boolean> {
  const unsynced = await countUnsyncedChanges();
  if (unsynced === 0) return true;

  return new Promise((resolve) => {
    Alert.alert(
      'Changes not synced',
      `${unsynced === 1 ? '1 change has' : `${unsynced} changes have`} not reached the server yet. Signing out deletes ${unsynced === 1 ? 'it' : 'them'} from this phone. Sync first if you can.`,
      [
        { text: 'Stay signed in', style: 'cancel', onPress: () => resolve(false) },
        { text: 'Sign out and delete', style: 'destructive', onPress: () => resolve(true) },
      ],
      { cancelable: true, onDismiss: () => resolve(false) },
    );
  });
}
