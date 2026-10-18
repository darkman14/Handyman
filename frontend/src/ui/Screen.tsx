import type { ReactNode } from 'react';
import { ScrollView, View } from 'react-native';
import { useTheme } from './useTheme';

export function Screen({ children }: { children: ReactNode }) {
  const { colors, spacing, layout } = useTheme();
  return (
    <ScrollView
      style={{ flex: 1, backgroundColor: colors.background }}
      contentContainerStyle={{ alignItems: 'center', padding: spacing.lg }}
    >
      <View style={{ width: '100%', maxWidth: layout.maxContentWidth, gap: spacing.lg }}>{children}</View>
    </ScrollView>
  );
}
