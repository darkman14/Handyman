const PUBLIC_PATHS = ['/', '/code'];
export const DEFAULT_HOME = '/home';

export const isPublicPath = (pathname: string) => PUBLIC_PATHS.includes(pathname);

// Backslashes and control characters are rejected (browsers treat "/\host" like "//host").
const hasUnsafeChar = (s: string) =>
  [...s].some((c) => c === '\\' || c.charCodeAt(0) < 32 || c.charCodeAt(0) === 127);

/** Returns the target only when it is an internal app path; otherwise null (no open redirects). */
export function safeRedirect(target: unknown): string | null {
  if (typeof target !== 'string') return null;
  if (!target.startsWith('/') || target.startsWith('//')) return null;
  if (hasUnsafeChar(target)) return null;
  const path = target.split(/[?#]/)[0];
  if (isPublicPath(path)) return null;
  return target;
}
