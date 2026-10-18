import { Text, View } from 'react-native';
import { useTheme } from './useTheme';

type Props = { label: string; tone?: 'neutral' | 'success' | 'warning' | 'danger' };

export function Badge({ label, tone = 'neutral' }: Props) {
  const { colors, spacing, radius, type } = useTheme();
  const color = tone === 'neutral' ? colors.textMuted : colors[tone];
  return (
    <View
      accessible
      accessibilityLabel={label}
      style={{
        alignSelf: 'flex-start',
        paddingHorizontal: spacing.sm,
        paddingVertical: spacing.xs,
        borderRadius: radius.pill,
        borderWidth: 1,
        borderColor: color,
      }}
    >
      <Text style={[type.caption, { color }]}>{label}</Text>
    </View>
  );
}
