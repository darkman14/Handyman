import { Modal, View } from 'react-native';
import { Button } from './Button';
import { Text } from './Text';
import { useTheme } from './useTheme';

type Props = {
  visible: boolean;
  title: string;
  confirmLabel: string;
  cancelLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
};

// Works the same on iOS, Android and web (Alert.alert does nothing on web).
export function ConfirmDialog({ visible, title, confirmLabel, cancelLabel, onConfirm, onCancel }: Props) {
  const { colors, spacing, radius, layout } = useTheme();
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onCancel}>
      <View
        style={{
          flex: 1,
          alignItems: 'center',
          justifyContent: 'center',
          padding: spacing.lg,
          backgroundColor: colors.scrim,
        }}
      >
        <View
          accessibilityViewIsModal
          aria-modal
          role="alertdialog"
          aria-label={title}
          style={{
            width: '100%',
            maxWidth: layout.maxContentWidth / 2,
            gap: spacing.lg,
            padding: spacing.xl,
            borderRadius: radius.lg,
            backgroundColor: colors.surface,
          }}
        >
          <Text variant="heading" role="header">{title}</Text>
          <Button title={confirmLabel} onPress={onConfirm} />
          <Button variant="secondary" title={cancelLabel} onPress={onCancel} />
        </View>
      </View>
    </Modal>
  );
}
