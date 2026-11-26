import { Redirect, useLocalSearchParams, useRouter } from 'expo-router';
import { useState } from 'react';
import { en } from '@/i18n/en';
import { Button, Card, Screen, Text, TextField } from '@/ui';
import { formatCountdown, useCooldown } from './useCooldown';
import { formatPhone, normalizeSerbianPhone } from './phone';
import { useRequestCode } from './useRequestCode';
import { useVerifyCode, type VerifyCodeError } from './useVerifyCode';

const CODE_LENGTH = 6;
const RESEND_SECONDS = 60;

const verifyMessages: Record<VerifyCodeError, string> = {
  invalid: en.auth.codeInvalid,
  rateLimited: en.auth.verifyRateLimited,
  failed: en.auth.verifyFailed,
};

export function CodeScreen() {
  const { phone: param } = useLocalSearchParams<{ phone?: string }>();
  const phone = typeof param === 'string' ? normalizeSerbianPhone(param) : null;
  // No valid number means no code was requested: go back to sign-in.
  if (!phone) return <Redirect href="/" />;
  return <CodeForm phone={phone} />;
}

function CodeForm({ phone }: { phone: string }) {
  const router = useRouter();
  const [code, setCode] = useState('');
  const { verify, loading, error, clearError } = useVerifyCode();
  const resend = useRequestCode();
  const cooldown = useCooldown(RESEND_SECONDS);

  const onChange = (text: string) => {
    setCode(text.replace(/\D/g, '').slice(0, CODE_LENGTH));
    if (error) clearError();
  };

  const onVerify = async () => {
    if (code.length !== CODE_LENGTH) return;
    if (await verify(phone, code)) {
      router.replace('/home' as '/');
    } else {
      setCode('');
    }
  };

  const onResend = async () => {
    if (await resend.requestCode(phone)) cooldown.start();
  };

  const resendTitle = cooldown.left > 0
    ? en.auth.resendIn.replace('{time}', formatCountdown(cooldown.left))
    : en.auth.resend;
  const resendError = resend.error
    ? resend.error === 'rateLimited' ? en.auth.rateLimited : en.auth.sendFailed
    : null;

  return (
    <Screen>
      <Card>
        <Text variant="title" role="header">{en.auth.codeTitle}</Text>
        <Text muted>{en.auth.codeSentTo.replace('{phone}', formatPhone(phone))}</Text>
        <TextField
          label={en.auth.codeLabel}
          value={code}
          onChangeText={onChange}
          onSubmitEditing={onVerify}
          keyboardType="number-pad"
          returnKeyType="go"
          textContentType="oneTimeCode"
          autoComplete="one-time-code"
          maxLength={CODE_LENGTH * 2}
          editable={!loading}
          aria-busy={loading}
        />
        {error ? <Text tone="danger" role="alert">{verifyMessages[error]}</Text> : null}
        {resendError ? <Text tone="danger" role="alert">{resendError}</Text> : null}
        <Button
          title={en.auth.verify}
          onPress={onVerify}
          loading={loading}
          disabled={code.length !== CODE_LENGTH}
        />
        <Button
          variant="secondary"
          title={resendTitle}
          onPress={onResend}
          loading={resend.loading}
          disabled={cooldown.left > 0}
        />
        <Button
          variant="secondary"
          title={en.auth.changeNumber}
          onPress={() => router.replace({ pathname: '/' as '/', params: { phone } })}
          disabled={loading}
        />
      </Card>
    </Screen>
  );
}
