import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { useTheme } from './useTheme';

export function LoadingScreen({ label, overlay }: { label: string; overlay?: boolean }) {
  const { colors, spacing } = useTheme();
  return (
    <View
      accessible
      accessibilityLabel={label}
      accessibilityRole="progressbar"
      style={{
        ...(overlay ? StyleSheet.absoluteFill : { flex: 1 }),
        alignItems: 'center',
        justifyContent: 'center',
        gap: spacing.md,
        backgroundColor: colors.background,
      }}
    >
      <ActivityIndicator color={colors.primary} size="large" />
      <Text muted>{label}</Text>
    </View>
  );
}
