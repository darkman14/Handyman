import { ActivityIndicator, Pressable, Text } from 'react-native';
import { useTheme } from './useTheme';

type Props = {
  title: string;
  onPress: () => void;
  accessibilityLabel?: string;
  variant?: 'primary' | 'secondary';
  disabled?: boolean;
  loading?: boolean;
};

export function Button({ title, onPress, accessibilityLabel, variant = 'primary', disabled, loading }: Props) {
  const { colors, spacing, radius, type, layout } = useTheme();
  const primary = variant === 'primary';
  const inactive = !!disabled || !!loading;
  const fg = primary ? colors.onPrimary : colors.text;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? title}
      accessibilityState={{ disabled: inactive, busy: !!loading }}
      disabled={inactive}
      onPress={onPress}
      style={({ pressed }) => ({
        minHeight: layout.minTouchTarget,
        minWidth: layout.minTouchTarget,
        paddingHorizontal: spacing.lg,
        flexDirection: 'row',
        gap: spacing.sm,
        justifyContent: 'center',
        alignItems: 'center',
        borderRadius: radius.md,
        borderWidth: 1,
        borderColor: primary ? colors.primary : colors.border,
        backgroundColor: primary ? colors.primary : colors.surface,
        opacity: inactive ? 0.5 : pressed ? 0.85 : 1,
      })}
    >
      {loading ? <ActivityIndicator color={fg} /> : null}
      <Text style={[type.label, { color: fg }]}>{title}</Text>
    </Pressable>
  );
}
