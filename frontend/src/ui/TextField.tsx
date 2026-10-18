import { Text, TextInput, View, type TextInputProps } from 'react-native';
import { useTheme } from './useTheme';

type Props = Omit<TextInputProps, 'style'> & { label: string; error?: string };

export function TextField({ label, error, ...rest }: Props) {
  const { colors, spacing, radius, type, layout } = useTheme();
  return (
    <View style={{ gap: spacing.xs }}>
      <Text style={[type.label, { color: colors.text }]}>{label}</Text>
      <TextInput
        accessibilityLabel={label}
        placeholderTextColor={colors.textMuted}
        {...rest}
        style={[
          type.body,
          {
            minHeight: layout.minTouchTarget,
            paddingHorizontal: spacing.md,
            color: colors.text,
            backgroundColor: colors.surface,
            borderWidth: 1,
            borderRadius: radius.md,
            borderColor: error ? colors.danger : colors.border,
          },
        ]}
      />
      {error ? <Text style={[type.caption, { color: colors.danger }]}>{error}</Text> : null}
    </View>
  );
}
