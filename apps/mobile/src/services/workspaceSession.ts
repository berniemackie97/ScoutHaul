import AsyncStorage from '@react-native-async-storage/async-storage';

const activeOrganizationKey = 'scouthaul.activeOrganizationId';

export async function getActiveOrganizationId(): Promise<string | null> {
  return AsyncStorage.getItem(activeOrganizationKey);
}

export async function setActiveOrganizationId(organizationId: string | null): Promise<void> {
  if (organizationId) {
    await AsyncStorage.setItem(activeOrganizationKey, organizationId);
    return;
  }

  await AsyncStorage.removeItem(activeOrganizationKey);
}
