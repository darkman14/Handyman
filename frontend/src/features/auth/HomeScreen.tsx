import { useState } from 'react';
import { en } from '@/i18n/en';
import { Button, Card, ConfirmDialog, Screen, Text } from '@/ui';
import { useAuth } from './AuthProvider';
import { formatPhone } from './phone';

// Placeholder: the real home comes with E03. The route guard sends signed-out users to sign-in.
export function HomeScreen() {
  const { session, signOut } = useAuth();
  const [confirming, setConfirming] = useState(false);
  const phone = session?.user?.phone;

  const confirm = async () => {
    setConfirming(false);
    await signOut();
  };

  return (
    <Screen>
      <Card>
        <Text variant="title" role="header">{en.home.title}</Text>
        {phone ? <Text>{en.home.signedInAs.replace('{phone}', formatPhone(phone))}</Text> : null}
        <Text muted>{en.home.placeholder}</Text>
        <Button variant="secondary" title={en.home.signOut} onPress={() => setConfirming(true)} />
      </Card>
      <ConfirmDialog
        visible={confirming}
        title={en.home.signOutTitle}
        confirmLabel={en.home.signOut}
        cancelLabel={en.home.cancel}
        onConfirm={confirm}
        onCancel={() => setConfirming(false)}
      />
    </Screen>
  );
}
