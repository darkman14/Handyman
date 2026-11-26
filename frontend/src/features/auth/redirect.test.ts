import { safeRedirect } from './redirect';

describe('safeRedirect', () => {
  it.each(['/home', '/jobs/42', '/jobs?id=1'])('accepts internal path %p', (p) => {
    expect(safeRedirect(p)).toBe(p);
  });
  it.each([
    undefined, null, '', 'home', 'https://evil.com', '//evil.com', '/\\evil.com', 'javascript:alert(1)',
    '/', '/code', '/code?phone=1', '/a\nb',
  ])('rejects %p', (p) => {
    expect(safeRedirect(p)).toBeNull();
  });
});
