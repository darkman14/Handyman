import { Pressable, Text } from 'react-native';
import { useTheme } from './useTheme';

type Props = {
  title: string;
  onPress: () => void;
  accessibilityLabel?: string;
  variant?: 'primary' | 'secondary';
  disabled?: boolean;
};

export function Button({ title, onPress, accessibilityLabel, variant = 'primary', disabled }: Props) {
  const { colors, spacing, radius, type, layout } = useTheme();
  const primary = variant === 'primary';
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? title}
      accessibilityState={{ disabled: !!disabled }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => ({
        minHeight: layout.minTouchTarget,
        minWidth: layout.minTouchTarget,
        paddingHorizontal: spacing.lg,
        justifyContent: 'center',
        alignItems: 'center',
        borderRadius: radius.md,
        borderWidth: 1,
        borderColor: primary ? colors.primary : colors.border,
        backgroundColor: primary ? colors.primary : colors.surface,
        opacity: disabled ? 0.5 : pressed ? 0.85 : 1,
      })}
    >
      <Text style={[type.label, { color: primary ? colors.onPrimary : colors.text }]}>{title}</Text>
    </Pressable>
  );
}
