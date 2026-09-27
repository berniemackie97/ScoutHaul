import { Ionicons } from '@expo/vector-icons';
import { useState } from 'react';
import { ActivityIndicator, FlatList, Modal, Pressable, StyleSheet, TextInput, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import type { CallOptions } from '../api/client';
import { useAppTheme } from '../context/ThemeContext';
import { useHaptics } from '../hooks/useHaptics';
import { emptyMessage } from '../hooks/searchState';
import { useServerSearch } from '../hooks/useServerSearch';
import { Text } from './Text';
import { radius, spacing } from './theme';

type SearchPickerFieldProps<T> = {
  label: string;
  value: T | null;
  onChange: (value: T | null) => void;
  /** Must be stable: a module function, or wrapped in useCallback. */
  search: (query: string, options: CallOptions) => Promise<T[]>;
  idOf: (item: T) => string;
  titleOf: (item: T) => string;
  describe?: (item: T) => string | undefined;
  /** What the results are, for the empty state: "unsold item". */
  noun: string;
  placeholder?: string;
  required?: boolean;
  error?: string | null;
};

/**
 * A field whose options are searched on the server as the operator types, so every match is
 * reachable — not just the ones a screen could afford to load up front. Looks like SelectField.
 */
export function SearchPickerField<T>({
  label,
  value,
  onChange,
  search,
  idOf,
  titleOf,
  describe,
  noun,
  placeholder = 'Search…',
  required,
  error,
}: SearchPickerFieldProps<T>) {
  const { colors: c } = useAppTheme();
  const insets = useSafeAreaInsets();
  const haptics = useHaptics();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const state = useServerSearch(query, open, search);
  const empty = emptyMessage(state, noun);

  const close = () => {
    setOpen(false);
    setQuery('');
  };

  const choose = (item: T) => {
    haptics.selection();
    onChange(item);
    close();
  };

  return (
    <>
      <View style={styles.field}>
        <Text variant="caption" color={error ? c.danger : c.textSecondary}>
          {label}
          {required ? <Text variant="caption" color={c.danger}> *</Text> : null}
        </Text>
        <Pressable
          onPress={() => setOpen(true)}
          accessibilityRole="button"
          accessibilityLabel={`${label}: ${value ? titleOf(value) : 'not selected'}`}
          style={[styles.trigger, { borderColor: error ? c.danger : c.border, backgroundColor: c.surface }]}
        >
          <View style={styles.triggerText}>
            <Text variant="body" color={value ? c.text : c.textMuted} numberOfLines={1}>
              {value ? titleOf(value) : placeholder}
            </Text>
            {value && describe?.(value) ? (
              <Text variant="caption" color={c.textSecondary} numberOfLines={1}>
                {describe(value)}
              </Text>
            ) : null}
          </View>
          {value ? (
            <Pressable
              onPress={(event) => {
                event.stopPropagation();
                onChange(null);
              }}
              hitSlop={8}
              accessibilityLabel={`Clear ${label}`}
            >
              <Ionicons name="close-circle" size={18} color={c.textMuted} />
            </Pressable>
          ) : null}
          <Ionicons name="search-outline" size={16} color={c.textMuted} />
        </Pressable>
        {error ? <Text variant="caption" color={c.danger}>{error}</Text> : null}
      </View>

      <Modal visible={open} animationType="slide" presentationStyle="pageSheet" onRequestClose={close}>
        <View style={[styles.modal, { backgroundColor: c.background }]}>
          <View style={[styles.modalHeader, { borderBottomColor: c.borderLight }]}>
            <Text variant="subtitle">{label}</Text>
            <Pressable onPress={close} hitSlop={8} accessibilityLabel="Close">
              <Ionicons name="close" size={24} color={c.textSecondary} />
            </Pressable>
          </View>

          <View style={[styles.searchRow, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}>
            <Ionicons name="search-outline" size={18} color={c.textMuted} />
            <TextInput
              style={[styles.searchInput, { color: c.text }]}
              value={query}
              onChangeText={setQuery}
              placeholder="Title, brand, barcode or location"
              placeholderTextColor={c.textMuted}
              autoFocus
              autoCorrect={false}
              returnKeyType="search"
              clearButtonMode="while-editing"
            />
            {state.status === 'searching' ? <ActivityIndicator size="small" color={c.textMuted} /> : null}
          </View>

          {state.status === 'searching' && state.results.length === 0 ? (
            <Text variant="caption" color={c.textSecondary} style={styles.notice}>Searching…</Text>
          ) : null}
          {state.status === 'failed' ? (
            <View style={styles.failure}>
              <Text variant="caption" color={c.danger} style={styles.failureText} accessibilityRole="alert">
                {state.error}
              </Text>
              <Pressable onPress={state.retry} hitSlop={8} accessibilityRole="button">
                <Text variant="caption" color={c.primary}>Try again</Text>
              </Pressable>
            </View>
          ) : null}
          {empty ? <Text variant="caption" color={c.textSecondary} style={styles.notice}>{empty}</Text> : null}

          <FlatList
            data={state.results}
            keyExtractor={idOf}
            keyboardShouldPersistTaps="handled"
            contentContainerStyle={[styles.optionList, { paddingBottom: insets.bottom + spacing.lg }]}
            renderItem={({ item }) => {
              const isSelected = value !== null && idOf(value) === idOf(item);
              return (
                <Pressable
                  onPress={() => choose(item)}
                  style={({ pressed }) => [
                    styles.optionRow,
                    { backgroundColor: isSelected ? c.primaryLight : pressed ? c.surfaceAlt : c.surface },
                  ]}
                  accessibilityRole="radio"
                  accessibilityState={{ selected: isSelected }}
                >
                  <View style={styles.optionContent}>
                    <Text variant="bodyBold" color={isSelected ? c.primary : c.text}>{titleOf(item)}</Text>
                    {describe?.(item) ? (
                      <Text variant="caption" color={c.textSecondary} numberOfLines={1}>{describe(item)}</Text>
                    ) : null}
                  </View>
                  {isSelected ? <Ionicons name="checkmark-circle" size={22} color={c.primary} /> : null}
                </Pressable>
              );
            }}
          />
        </View>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  field: {
    gap: spacing.xs,
  },
  trigger: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    minHeight: 46,
    borderWidth: 1.5,
    borderRadius: radius.md,
    paddingLeft: spacing.md,
    paddingRight: spacing.sm,
    paddingVertical: spacing.xs,
  },
  triggerText: {
    flex: 1,
  },
  modal: {
    flex: 1,
  },
  modalHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.lg,
    borderBottomWidth: 1,
  },
  searchRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    margin: spacing.lg,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.md,
    borderWidth: 1,
  },
  searchInput: {
    flex: 1,
    fontSize: 15,
    paddingVertical: spacing.xs,
  },
  notice: {
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.sm,
  },
  failure: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.sm,
  },
  failureText: {
    flex: 1,
  },
  optionList: {
    paddingHorizontal: spacing.md,
    paddingTop: spacing.sm,
  },
  optionRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.md,
    borderRadius: radius.md,
    gap: spacing.md,
    marginVertical: 1,
  },
  optionContent: {
    flex: 1,
    gap: spacing.xxs,
  },
});
