import { formatPhone, normalizeSerbianPhone as n } from './phone';

const SEEDED = '+381600000201';

describe('normalizeSerbianPhone', () => {
  it.each([
    ['0600000201'],
    ['060 000 0201'],
    ['060-000-0201'],
    ['+381600000201'],
    ['+381 60 000 0201'],
    ['+381-60-000-0201'],
    ['+381 060 000 0201'],
    ['00381600000201'],
    ['  +381 60 000 0201  '],
    ['+381+381 60 000 0201'],
  ])('accepts %s', (input) => {
    expect(n(input)).toBe(SEEDED);
  });

  it('accepts 8 and 9 digit national mobiles', () => {
    expect(n('0641234567')).toBe('+381641234567');
    expect(n('064123456')).toBe('+38164123456');
  });

  it.each([
    [''], ['   '], ['+381'], ['abc'], ['+44 7700 900123'], ['+1 202 555 0100'], ['0112345678'],
    ['+381 6'], ['06000002011234'], ['+381' + '6'.repeat(60)], ['060 000 02a1'], ['5551234'],
    ['+381 11 234 5678'],
  ])('rejects %p', (input) => {
    expect(n(input)).toBeNull();
  });
});

describe('formatPhone', () => {
  it.each([
    ['+381600000201', '+381 60 000 0201'],
    ['381600000201', '+381 60 000 0201'],
    ['+38160123456', '+381 60 123 456'],
    ['garbage', '+garbage'],
  ])('formats %s as %s', (input, expected) => {
    expect(formatPhone(input)).toBe(expected);
  });
});
