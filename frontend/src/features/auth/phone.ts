export const SERBIA_PREFIX = '+381';

// Serbian mobile numbers: 6x followed by 6-7 digits (national form without the leading 0).
const NATIONAL_MOBILE = /^6\d{7,8}$/;

/** Returns the number as +381XXXXXXXXX, or null when it is not a valid Serbian mobile number. */
export function normalizeSerbianPhone(input: string): string | null {
  if (typeof input !== 'string' || input.length > 40) return null;
  let value = input.trim();
  // Pasting into the prefilled "+381" field can leave several prefixes: keep the last one.
  const lastPlus = value.lastIndexOf('+');
  if (lastPlus > 0) value = value.slice(lastPlus);
  value = value.replace(/[\s\-().]/g, '');
  if (!/^\+?\d+$/.test(value)) return null;

  let national: string;
  if (value.startsWith('+381')) national = value.slice(4);
  else if (value.startsWith('00381')) national = value.slice(5);
  else if (value.startsWith('+')) return null;
  else if (value.startsWith('0') && !value.startsWith('00')) national = value.slice(1);
  else return null;

  if (national.startsWith('0')) national = national.slice(1);
  return NATIONAL_MOBILE.test(national) ? `${SERBIA_PREFIX}${national}` : null;
}

/** Formats +381600000201 as "+381 60 000 0201". Returns the input unchanged when it does not look like one. */
export function formatPhone(input: string): string {
  const value = input.startsWith('+') ? input : `+${input}`;
  const m = /^\+381(\d{2})(\d{3})(\d{3,4})$/.exec(value);
  return m ? `${SERBIA_PREFIX} ${m[1]} ${m[2]} ${m[3]}` : value;
}
