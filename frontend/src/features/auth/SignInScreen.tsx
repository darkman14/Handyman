import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from 'expo-router';
import { Controller, useForm } from 'react-hook-form';
import { z } from 'zod';
import { en } from '@/i18n/en';
import { Button, Card, Screen, Text, TextField } from '@/ui';
import { normalizeSerbianPhone, SERBIA_PREFIX } from './phone';
import { useRequestCode } from './useRequestCode';

const schema = z.object({
  phone: z.string().refine((v) => normalizeSerbianPhone(v) !== null, en.auth.invalidPhone),
});
type Values = z.infer<typeof schema>;

export function SignInScreen() {
  const router = useRouter();
  const { requestCode, loading, error } = useRequestCode();
  const { control, handleSubmit } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { phone: SERBIA_PREFIX },
  });

  const onSubmit = handleSubmit(async ({ phone }) => {
    const normalized = normalizeSerbianPhone(phone);
    if (!normalized) return;
    if (await requestCode(normalized)) {
      router.push({ pathname: '/code' as '/', params: { phone: normalized } });
    }
  });

  return (
    <Screen>
      <Card>
        <Text variant="title" role="header">{en.auth.signInTitle}</Text>
        <Text muted>{en.auth.signInHelp}</Text>
        <Controller
          control={control}
          name="phone"
          render={({ field, fieldState }) => (
            <TextField
              label={en.auth.phoneLabel}
              value={field.value}
              onChangeText={field.onChange}
              onBlur={field.onBlur}
              keyboardType="phone-pad"
              textContentType="telephoneNumber"
              autoComplete="tel"
              editable={!loading}
              error={fieldState.error?.message}
            />
          )}
        />
        {error ? (
          <Text tone="danger" role="alert">
            {error === 'rateLimited' ? en.auth.rateLimited : en.auth.sendFailed}
          </Text>
        ) : null}
        <Button title={en.auth.sendCode} onPress={onSubmit} loading={loading} />
      </Card>
    </Screen>
  );
}
