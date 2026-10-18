import { useColorScheme } from 'react-native';
import { layout, palette, radius, spacing, type, type Colors } from './theme';

export function useTheme() {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light';
  const colors: Colors = palette[scheme];
  return { scheme, colors, spacing, radius, type, layout };
}
