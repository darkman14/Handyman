import { Redirect } from 'expo-router';
import { en } from '@/i18n/en';
import { Card, Screen, Text } from '@/ui';
import { formatPhone } from './phone';
import { useSessionPhone } from './useSessionPhone';

// Placeholder: the real home comes with E03.
export function HomeScreen() {
  const phone = useSessionPhone();
  if (phone === null) return <Redirect href="/" />;
  return (
    <Screen>
      <Card>
        <Text variant="title" role="header">{en.home.title}</Text>
        <Text>{phone === undefined ? en.home.loading : en.home.signedInAs.replace('{phone}', formatPhone(phone))}</Text>
        <Text muted>{en.home.placeholder}</Text>
      </Card>
    </Screen>
  );
}
