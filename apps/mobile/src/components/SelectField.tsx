import { Ionicons } from '@expo/vector-icons';
import { useCallback, useState } from 'react';
import {
  FlatList,
  Modal,
  Pressable,
  StyleSheet,
  TextInput,
  View,
  type ViewStyle,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Text } from './Text';
import { radius, spacing } from './theme';
import { useAppTheme } from '../context/ThemeContext';
import { useHaptics } from '../hooks/useHaptics';

export type SelectOption = {
  value: string;
  label: string;
  description?: string;
  icon?: keyof typeof Ionicons.glyphMap;
  group?: string;
};

type SelectFieldProps = {
  label: string;
  value: string;
  options: SelectOption[];
  onChange: (value: string) => void;
  placeholder?: string;
  required?: boolean;
  error?: string | null;
  clearable?: boolean;
  searchable?: boolean;
  containerStyle?: ViewStyle;
};

export function SelectField({
  label,
  value,
  options,
  onChange,
  placeholder = 'Select...',
  required,
  error,
  clearable = true,
  searchable = false,
  containerStyle,
}: SelectFieldProps) {
  const { colors: c, shadows: s } = useAppTheme();
  const [open, setOpen] = useState(false);

  const selected = options.find((o) => o.value === value);

  return (
    <>
      <View style={[styles.field, containerStyle]}>
        <View style={styles.labelRow}>
          <Text variant="caption" color={error ? c.danger : c.textSecondary}>
            {label}
            {required ? <Text variant="caption" color={c.danger}> *</Text> : null}
          </Text>
        </View>
        <Pressable
          onPress={() => setOpen(true)}
          accessibilityRole="button"
          accessibilityLabel={`${label}: ${selected?.label ?? 'not selected'}`}
          style={[
            styles.trigger,
            {
              borderColor: error ? c.danger : c.border,
              backgroundColor: c.surface,
            },
          ]}
        >
          {selected ? (
            <View style={styles.selectedRow}>
              {selected.icon ? (
                <Ionicons name={selected.icon} size={16} color={c.primary} />
              ) : null}
              <Text variant="body" numberOfLines={1} style={styles.selectedText}>
                {selected.label}
              </Text>
            </View>
          ) : (
            <Text variant="body" color={c.textMuted} numberOfLines={1} style={styles.selectedText}>
              {placeholder}
            </Text>
          )}
          <View style={styles.triggerRight}>
            {value && clearable ? (
              <Pressable
                onPress={(e) => {
                  e.stopPropagation();
                  onChange('');
                }}
                hitSlop={8}
                style={styles.clearBtn}
              >
                <Ionicons name="close-circle" size={18} color={c.textMuted} />
              </Pressable>
            ) : null}
            <Ionicons name="chevron-down" size={16} color={c.textMuted} />
          </View>
        </Pressable>
        {error ? <Text variant="caption" color={c.danger}>{error}</Text> : null}
      </View>

      <SelectModal
        visible={open}
        title={label}
        options={options}
        value={value}
        searchable={searchable}
        onSelect={(v) => { onChange(v); setOpen(false); }}
        onClose={() => setOpen(false)}
      />
    </>
  );
}

function SelectModal({
  visible,
  title,
  options,
  value,
  searchable,
  onSelect,
  onClose,
}: {
  visible: boolean;
  title: string;
  options: SelectOption[];
  value: string;
  searchable: boolean;
  onSelect: (value: string) => void;
  onClose: () => void;
}) {
  const { colors: c, shadows: s } = useAppTheme();
  const insets = useSafeAreaInsets();
  const haptics = useHaptics();
  const [search, setSearch] = useState('');

  const filtered = searchable && search.trim()
    ? options.filter((o) =>
        o.label.toLowerCase().includes(search.toLowerCase()) ||
        o.description?.toLowerCase().includes(search.toLowerCase()),
      )
    : options;

  const groups = groupOptions(filtered);

  const handleSelect = useCallback((optionValue: string) => {
    haptics.selection();
    onSelect(optionValue);
    setSearch('');
  }, [onSelect]);

  const handleClose = useCallback(() => {
    setSearch('');
    onClose();
  }, [onClose]);

  return (
    <Modal
      visible={visible}
      animationType="slide"
      presentationStyle="pageSheet"
      onRequestClose={handleClose}
    >
      <View style={[styles.modal, { backgroundColor: c.background }]}>
        <View style={[styles.modalHeader, { borderBottomColor: c.borderLight }]}>
          <Text variant="subtitle">{title}</Text>
          <Pressable onPress={handleClose} hitSlop={8} accessibilityLabel="Close">
            <Ionicons name="close" size={24} color={c.textSecondary} />
          </Pressable>
        </View>

        {searchable ? (
          <View style={[styles.searchRow, { backgroundColor: c.surfaceAlt, borderColor: c.border }]}>
            <Ionicons name="search-outline" size={18} color={c.textMuted} />
            <TextInput
              style={[styles.searchInput, { color: c.text }]}
              value={search}
              onChangeText={setSearch}
              placeholder="Search..."
              placeholderTextColor={c.textMuted}
              autoFocus
              returnKeyType="search"
              clearButtonMode="while-editing"
            />
          </View>
        ) : null}

        <FlatList
          data={groups}
          keyExtractor={(item) => item.type === 'group' ? `group-${item.label}` : `opt-${item.option.value}`}
          contentContainerStyle={[styles.optionList, { paddingBottom: insets.bottom + spacing.lg }]}
          renderItem={({ item }) => {
            if (item.type === 'group') {
              return (
                <View style={styles.groupHeader}>
                  <Text variant="label" color={c.textMuted}>{item.label}</Text>
                </View>
              );
            }

            const opt = item.option;
            const isSelected = opt.value === value;

            return (
              <Pressable
                onPress={() => handleSelect(opt.value)}
                style={({ pressed }) => [
                  styles.optionRow,
                  { backgroundColor: isSelected ? c.primaryLight : pressed ? c.surfaceAlt : c.surface },
                ]}
                accessibilityRole="radio"
                accessibilityState={{ selected: isSelected }}
              >
                {opt.icon ? (
                  <View style={[styles.optionIcon, { backgroundColor: c.surfaceAlt }]}>
                    <Ionicons name={opt.icon} size={18} color={isSelected ? c.primary : c.textSecondary} />
                  </View>
                ) : null}
                <View style={styles.optionContent}>
                  <Text variant="bodyBold" color={isSelected ? c.primary : c.text}>
                    {opt.label}
                  </Text>
                  {opt.description ? (
                    <Text variant="caption" color={c.textSecondary} numberOfLines={1}>
                      {opt.description}
                    </Text>
                  ) : null}
                </View>
                {isSelected ? (
                  <Ionicons name="checkmark-circle" size={22} color={c.primary} />
                ) : (
                  <View style={[styles.radioOuter, { borderColor: c.border }]} />
                )}
              </Pressable>
            );
          }}
        />
      </View>
    </Modal>
  );
}

type GroupedItem =
  | { type: 'group'; label: string }
  | { type: 'option'; option: SelectOption };

function groupOptions(options: SelectOption[]): GroupedItem[] {
  const hasGroups = options.some((o) => o.group);
  if (!hasGroups) return options.map((o) => ({ type: 'option', option: o }));

  const items: GroupedItem[] = [];
  let lastGroup = '';
  for (const option of options) {
    const group = option.group ?? '';
    if (group !== lastGroup) {
      items.push({ type: 'group', label: group || 'Other' });
      lastGroup = group;
    }
    items.push({ type: 'option', option });
  }
  return items;
}

const styles = StyleSheet.create({
  field: {
    gap: spacing.xs,
  },
  labelRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xxs,
  },
  trigger: {
    flexDirection: 'row',
    alignItems: 'center',
    minHeight: 46,
    borderWidth: 1.5,
    borderRadius: radius.md,
    paddingLeft: spacing.md,
    paddingRight: spacing.sm,
  },
  selectedRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    flex: 1,
  },
  selectedText: {
    flex: 1,
  },
  triggerRight: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xxs,
  },
  clearBtn: {
    padding: spacing.xxs,
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
  optionList: {
    paddingHorizontal: spacing.md,
    paddingTop: spacing.sm,
  },
  groupHeader: {
    paddingHorizontal: spacing.md,
    paddingTop: spacing.lg,
    paddingBottom: spacing.xs,
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
  optionIcon: {
    width: 36,
    height: 36,
    borderRadius: radius.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  optionContent: {
    flex: 1,
    gap: spacing.xxs,
  },
  radioOuter: {
    width: 22,
    height: 22,
    borderRadius: 11,
    borderWidth: 2,
  },
});
