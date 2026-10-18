import { Pressable, Text, View } from 'react-native';
import { useTheme } from './useTheme';

type Props = { value: number; onChange?: (value: number) => void };

export function Stars({ value, onChange }: Props) {
  const { colors, type, layout } = useTheme();
  return (
    <View accessible={!onChange} accessibilityLabel={`Rating ${value} of 5`} style={{ flexDirection: 'row' }}>
      {[1, 2, 3, 4, 5].map((n) => {
        const star = (
          <Text style={[type.heading, { color: n <= value ? colors.star : colors.border }]}>
            {n <= value ? '★' : '☆'}
          </Text>
        );
        return onChange ? (
          <Pressable
            key={n}
            accessibilityRole="button"
            accessibilityLabel={`${n} star${n > 1 ? 's' : ''}`}
            onPress={() => onChange(n)}
            style={{
              minWidth: layout.minTouchTarget,
              minHeight: layout.minTouchTarget,
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            {star}
          </Pressable>
        ) : (
          <View key={n}>{star}</View>
        );
      })}
    </View>
  );
}
