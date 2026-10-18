export type ColorScheme = 'light' | 'dark';

export const palette = {
  light: {
    background: '#F7F8FA',
    surface: '#FFFFFF',
    border: '#D9DDE3',
    text: '#14181F',
    textMuted: '#5B6573',
    primary: '#1D6FE0',
    onPrimary: '#FFFFFF',
    success: '#1B7F4B',
    warning: '#9A6200',
    danger: '#C0332B',
    star: '#E5A400',
    focus: '#1D6FE0',
  },
  dark: {
    background: '#0E1116',
    surface: '#181C23',
    border: '#2B313B',
    text: '#F1F3F6',
    textMuted: '#A3ACB9',
    primary: '#5B9BFF',
    onPrimary: '#0E1116',
    success: '#4CC38A',
    warning: '#E0A040',
    danger: '#FF7A70',
    star: '#F2B705',
    focus: '#5B9BFF',
  },
} as const;

export type Colors = { [K in keyof (typeof palette)['light']]: string };

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;
export const radius = { sm: 6, md: 10, lg: 16, pill: 999 } as const;
export const type = {
  title: { fontSize: 28, lineHeight: 34, fontWeight: '700' },
  heading: { fontSize: 20, lineHeight: 26, fontWeight: '600' },
  body: { fontSize: 16, lineHeight: 22, fontWeight: '400' },
  label: { fontSize: 14, lineHeight: 18, fontWeight: '600' },
  caption: { fontSize: 12, lineHeight: 16, fontWeight: '400' },
} as const;
export const layout = { maxContentWidth: 720, minTouchTarget: 44 } as const;
