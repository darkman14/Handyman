import { useCallback, useEffect, useState } from 'react';

/** Counts down whole seconds after start(); formatted as m:ss by formatCountdown. */
export function useCooldown(seconds: number) {
  const [left, setLeft] = useState(0);

  const running = left > 0;
  useEffect(() => {
    if (!running) return;
    const id = setInterval(() => setLeft((s) => Math.max(0, s - 1)), 1000);
    return () => clearInterval(id);
  }, [running]);

  const start = useCallback(() => setLeft(seconds), [seconds]);
  return { left, start };
}

export function formatCountdown(total: number): string {
  const s = String(total % 60).padStart(2, '0');
  return `${Math.floor(total / 60)}:${s}`;
}
