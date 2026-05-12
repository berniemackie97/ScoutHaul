import { Ionicons } from '@expo/vector-icons';
import { useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../src/api/client';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { ErrorBanner } from '../src/components/ErrorBanner';
import { FilterChips } from '../src/components/FilterChips';
import { SkeletonList } from '../src/components/Skeleton';
import { Screen } from '../src/components/Screen';
import { SectionHeader } from '../src/components/SectionHeader';
import { StatusPill } from '../src/components/StatusPill';
import { Text } from '../src/components/Text';
import { TextField } from '../src/components/TextField';
import { radius, spacing, type ToneKey } from '../src/components/theme';
import { useColors } from '../src/context/ThemeContext';
import type { ResaleTask, WorkTaskStatus } from '../src/domain/types';

type TaskFilter = 'Active' | 'All' | 'Completed';

const filterOptions: { value: TaskFilter; label: string }[] = [
  { value: 'Active', label: 'Active' },
  { value: 'All', label: 'All' },
  { value: 'Completed', label: 'Done' },
];

function taskTone(status: WorkTaskStatus): ToneKey {
  if (status === 'Completed') return 'success';
  if (status === 'Blocked') return 'danger';
  if (status === 'InProgress') return 'info';
  return 'neutral';
}

const nextAction: Record<WorkTaskStatus, { label: string; status: WorkTaskStatus; icon: keyof typeof Ionicons.glyphMap }> = {
  Open: { label: 'Start', status: 'InProgress', icon: 'play-outline' },
  InProgress: { label: 'Done', status: 'Completed', icon: 'checkmark-outline' },
  Blocked: { label: 'Unblock', status: 'InProgress', icon: 'arrow-redo-outline' },
  Completed: { label: 'Reopen', status: 'Open', icon: 'refresh-outline' },
  Cancelled: { label: 'Reopen', status: 'Open', icon: 'refresh-outline' },
};

export default function TasksScreen() {
  const c = useColors();
  const [tasks, setTasks] = useState<ResaleTask[]>([]);
  const [title, setTitle] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [filter, setFilter] = useState<TaskFilter>('Active');

  async function load(showRefresh = false) {
    if (showRefresh) setRefreshing(true);
    try {
      setTasks(await api.tasks());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Tasks unavailable.');
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  }

  useFocusEffect(useCallback(() => { void load(); }, []));

  const filtered = useMemo(() => {
    if (filter === 'Active') return tasks.filter((t) => t.status !== 'Completed' && t.status !== 'Cancelled');
    if (filter === 'Completed') return tasks.filter((t) => t.status === 'Completed');
    return tasks;
  }, [tasks, filter]);

  async function createTask() {
    if (!title.trim()) {
      setError('Task title is required.');
      return;
    }
    try {
      await api.createTask({ title, description: null });
      setTitle('');
      setError(null);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to create task.');
    }
  }

  async function updateStatus(id: string, status: WorkTaskStatus) {
    try {
      await api.updateTaskStatus(id, status);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to update task.');
    }
  }

  return (
    <Screen refreshing={refreshing} onRefresh={() => void load(true)}>
      {error ? <ErrorBanner message={error} /> : null}

      <Card>
        <View style={styles.createRow}>
          <TextField
            label=""
            value={title}
            onChangeText={setTitle}
            placeholder="New task: photograph, test, ship..."
            containerStyle={styles.flex}
          />
          <Button
            label="Add"
            icon="add-outline"
            size="sm"
            disabled={!title.trim()}
            onPress={() => void createTask()}
          />
        </View>
      </Card>

      <FilterChips options={filterOptions} selected={filter} onSelect={setFilter} />

      {isLoading ? (
        <SkeletonList count={4} />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon="checkbox-outline"
          title={filter === 'Active' ? 'No active tasks' : 'No tasks'}
          message={filter === 'Active'
            ? 'All caught up. Add tasks for items that need work.'
            : 'Create tasks for resale work: test, clean, photograph, list, ship.'}
        />
      ) : (
        filtered.map((task) => {
          const action = nextAction[task.status];
          return (
            <Card key={task.id} style={styles.taskCard}>
              <View style={styles.taskHeader}>
                <View style={styles.taskInfo}>
                  <Text
                    variant="bodyBold"
                    color={task.status === 'Completed' ? c.textMuted : undefined}
                    style={task.status === 'Completed' ? styles.completed : null}
                  >
                    {task.title}
                  </Text>
                  <View style={styles.taskMeta}>
                    <StatusPill label={task.status} tone={taskTone(task.status)} size="sm" />
                    {task.dueAt ? (
                      <Text variant="caption">
                        Due {new Date(task.dueAt).toLocaleDateString()}
                      </Text>
                    ) : null}
                  </View>
                </View>
                <Pressable
                  onPress={() => void updateStatus(task.id, action.status)}
                  accessibilityRole="button"
                  accessibilityLabel={action.label}
                  style={[styles.quickAction, { backgroundColor: c.primaryLight }]}
                >
                  <Ionicons name={action.icon} size={22} color={c.primary} />
                </Pressable>
              </View>
            </Card>
          );
        })
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  createRow: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: spacing.sm,
  },
  flex: {
    flex: 1,
  },
  taskCard: {
    marginBottom: 0,
  },
  taskHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  taskInfo: {
    flex: 1,
    gap: spacing.xs,
  },
  taskMeta: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  completed: {
    textDecorationLine: 'line-through',
  },
  quickAction: {
    width: 44,
    height: 44,
    borderRadius: 22,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
