import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { ActionCard } from '../../src/components/ActionCard';
import { Screen } from '../../src/components/Screen';
import { SectionHeader } from '../../src/components/SectionHeader';
import { Text } from '../../src/components/Text';
import { spacing } from '../../src/components/theme';

export default function MoreScreen() {
  return (
    <Screen edges={['top', 'left', 'right']}>
      <Text variant="title">More</Text>

      <SectionHeader title="Workflow" />
      <ActionCard
        icon="wallet-outline"
        title="Budget"
        subtitle="Track spending and remaining budget"
        onPress={() => router.push('/budget')}
      />
      <ActionCard
        icon="pricetags-outline"
        title="Listing Queue"
        subtitle="Manage items through the listing workflow"
        onPress={() => router.push('/listing-queue')}
      />
      <ActionCard
        icon="paper-plane-outline"
        title="Sales & Shipping"
        subtitle="Record sales, pack orders, and move shipments forward"
        onPress={() => router.push('/sales')}
      />
      <ActionCard
        icon="checkbox-outline"
        title="Tasks"
        subtitle="To-do list for resale work"
        onPress={() => router.push('/tasks')}
      />

      <SectionHeader title="App" />
      <ActionCard
        icon="person-circle-outline"
        title="Account & Workspace"
        subtitle="Sign in, sign up, or keep using local mode"
        tone="primary"
        onPress={() => router.push('/account')}
      />
      <ActionCard
        icon="business-outline"
        title="Workspace Admin"
        subtitle="Organizations, teams, members, roles, and permissions"
        onPress={() => router.push('/workspace-admin')}
      />
      <ActionCard
        icon="settings-outline"
        title="Settings"
        subtitle="API, sync queue, and local drafts"
        onPress={() => router.push('/settings')}
      />
    </Screen>
  );
}
