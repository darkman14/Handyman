import type { ReactNode } from 'react';
import { Text as RNText } from 'react-native';
import { type } from './theme';
import { useTheme } from './useTheme';

type Props = { children: ReactNode; variant?: keyof typeof type; muted?: boolean };

export function Text({ children, variant = 'body', muted }: Props) {
  const { colors, type: t } = useTheme();
  return <RNText style={[t[variant], { color: muted ? colors.textMuted : colors.text }]}>{children}</RNText>;
}
