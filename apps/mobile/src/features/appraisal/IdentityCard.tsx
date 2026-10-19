import { Ionicons } from '@expo/vector-icons';
import { Image } from 'expo-image';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { Button, IconButton } from '../../components/Button';
import { Card } from '../../components/Card';
import { SelectField } from '../../components/SelectField';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useAppTheme } from '../../context/ThemeContext';
import { conditionOptions } from '../../domain/inventoryOptions';
import { certaintyLabel, certaintyTone, conditionFromGrade, sourceLabel } from './labels';
import type { Identification } from './types';

type IdentityCardProps = {
  item: Identification;
  photoUri: string | null;
  onCorrect?: (item: Identification) => void;
};

export function IdentityCard({ item, photoUri, onCorrect }: IdentityCardProps) {
  const { colors: c } = useAppTheme();
  const [editing, setEditing] = useState(false);
  const image = photoUri ?? item.imageUrl;
  const details = [item.brand, item.model, item.category].filter(Boolean).join(' · ');

  return (
    <Card>
      <View style={styles.row}>
        {image ? (
          <Image source={{ uri: image }} style={styles.thumb} contentFit="cover" accessibilityIgnoresInvertColors />
        ) : (
          <View style={[styles.thumb, styles.thumbEmpty, { backgroundColor: c.surfaceAlt }]}>
            <Ionicons name={item.barcode ? 'barcode-outline' : 'pricetag-outline'} size={26} color={c.textMuted} />
          </View>
        )}
        <View style={styles.text}>
          <Text variant="bodyBold" numberOfLines={3}>
            {item.title}
          </Text>
          {details ? <Text variant="caption" numberOfLines={2}>{details}</Text> : null}
          <View style={styles.pills}>
            <StatusPill label={certaintyLabel[item.certainty]} tone={certaintyTone[item.certainty]} size="sm" />
            <Text variant="caption" color={c.textMuted}>{sourceLabel[item.source]}</Text>
          </View>
        </View>
        {onCorrect && !editing ? (
          <IconButton icon="create-outline" label="Correct the details" onPress={() => setEditing(true)} />
        ) : null}
      </View>

      {item.condition ? <Text variant="caption">Condition: {item.condition}</Text> : null}
      {item.barcode ? <Text variant="caption" color={c.textMuted}>Barcode {item.barcode}</Text> : null}

      {item.certainty !== 'High' && item.missingInformation.length > 0 ? (
        <View style={[styles.missing, { backgroundColor: c.warningLight }]}>
          <Text variant="caption" color={c.warningDark}>To pin it down, check:</Text>
          {item.missingInformation.map((line) => (
            <Text key={line} variant="caption" color={c.warningDark}>• {line}</Text>
          ))}
        </View>
      ) : null}

      {editing && onCorrect ? (
        <IdentityEditor
          item={item}
          onCancel={() => setEditing(false)}
          onSave={(corrected) => {
            setEditing(false);
            onCorrect(corrected);
          }}
        />
      ) : null}
    </Card>
  );
}

function IdentityEditor({
  item,
  onCancel,
  onSave,
}: {
  item: Identification;
  onCancel: () => void;
  onSave: (item: Identification) => void;
}) {
  const [title, setTitle] = useState(item.title);
  const [brand, setBrand] = useState(item.brand ?? '');
  const [model, setModel] = useState(item.model ?? '');
  const [condition, setCondition] = useState(
    conditionFromGrade(item.conditionGrade) ?? conditionOptions.find((option) => option.value === item.condition)?.value ?? '',
  );
  const canSave = title.trim().length > 0;

  return (
    <View style={styles.editor}>
      <TextField label="What it is" value={title} onChangeText={setTitle} autoCapitalize="words" />
      <TextField label="Brand" value={brand} onChangeText={setBrand} autoCapitalize="words" />
      <TextField label="Model or number" value={model} onChangeText={setModel} autoCapitalize="characters" />
      <SelectField label="Condition" value={condition} options={conditionOptions} onChange={setCondition} clearable />
      <View style={styles.editorActions}>
        <Button label="Cancel" variant="ghost" size="sm" onPress={onCancel} />
        <Button
          label="Price again"
          icon="refresh"
          size="sm"
          disabled={!canSave}
          onPress={() =>
            onSave({
              ...item,
              title: title.trim(),
              brand: brand.trim() || null,
              model: model.trim() || null,
              condition: condition || null,
              // A person confirmed it, so it is no longer a guess.
              certainty: 'High',
            })
          }
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    gap: spacing.md,
    alignItems: 'flex-start',
  },
  thumb: {
    width: 84,
    height: 84,
    borderRadius: radius.md,
  },
  thumbEmpty: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  text: {
    flex: 1,
    gap: spacing.xs,
  },
  pills: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    flexWrap: 'wrap',
  },
  missing: {
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.xxs,
  },
  editor: {
    gap: spacing.md,
    paddingTop: spacing.sm,
  },
  editorActions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: spacing.sm,
  },
});
