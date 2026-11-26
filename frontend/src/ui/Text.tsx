import type { ReactNode } from 'react';
import { Text as RNText } from 'react-native';
import { type } from './theme';
import { useTheme } from './useTheme';

type Props = {
  children: ReactNode;
  variant?: keyof typeof type;
  muted?: boolean;
  tone?: 'danger';
  role?: 'alert' | 'header';
};

export function Text({ children, variant = 'body', muted, tone, role }: Props) {
  const { colors, type: t } = useTheme();
  const color = tone === 'danger' ? colors.danger : muted ? colors.textMuted : colors.text;
  return (
    <RNText accessibilityRole={role} style={[t[variant], { color }]}>
      {children}
    </RNText>
  );
}
