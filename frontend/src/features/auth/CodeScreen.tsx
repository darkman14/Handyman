import { useLocalSearchParams, useRouter } from 'expo-router';
import { en } from '@/i18n/en';
import { Button, Card, Screen, Text } from '@/ui';

// Placeholder: entering and verifying the code is US-0008.
export function CodeScreen() {
  const { phone } = useLocalSearchParams<{ phone?: string }>();
  const router = useRouter();
  return (
    <Screen>
      <Card>
        <Text variant="title" role="header">{en.auth.codeTitle}</Text>
        <Text muted>{en.auth.codeSentTo}</Text>
        <Text variant="heading">{phone ?? ''}</Text>
        <Text muted>{en.auth.codeComingSoon}</Text>
        <Button variant="secondary" title={en.auth.changeNumber} onPress={() => router.back()} />
      </Card>
    </Screen>
  );
}
