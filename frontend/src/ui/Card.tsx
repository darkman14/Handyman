import type { ReactNode } from 'react';
import { View } from 'react-native';
import { useTheme } from './useTheme';

export function Card({ children, accessibilityLabel }: { children: ReactNode; accessibilityLabel?: string }) {
  const { colors, spacing, radius } = useTheme();
  return (
    <View
      accessible={!!accessibilityLabel}
      accessibilityLabel={accessibilityLabel}
      style={{
        backgroundColor: colors.surface,
        borderColor: colors.border,
        borderWidth: 1,
        borderRadius: radius.lg,
        padding: spacing.lg,
        gap: spacing.sm,
      }}
    >
      {children}
    </View>
  );
}
