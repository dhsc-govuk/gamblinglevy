// Shared GBP formatter. Renders an em dash for null amounts; otherwise a £ value with up to two
// decimals and en-GB thousands separators. Single source of truth for money display across the app.
// A negative renders as "-£1,500", not "£-1,500": the sign belongs outside the symbol (GDS money
// formatting). Reachable since "Allocations remaining for the year" can go negative when an LA overspends.
export const formatGBP = (amount: number | null): string => {
  if (amount === null) {
    return '—';
  }
  const magnitude = Math.abs(amount).toLocaleString('en-GB', { maximumFractionDigits: 2 });
  return `${amount < 0 ? '-' : ''}£${magnitude}`;
};

// The rules every money input in the app is held to: whole pounds only, £0 or more (the design tells users to
// enter £0 for nothing spent), capped where the backend caps. They live here rather than beside either caller
// because two screens now enforce them — "Record funding spent" and the activity record's Currency questions
// (BLUM-7860) — and the messages differ while the rules must not. Each caller words its own errors: the funding
// screens name the activity and quarter, a form question names itself.
export const MONEY_PATTERN = /^\d+$/;

// Matches FundingSpendValidator/ActivityAnswerValidator: keeps amounts inside the numeric(18,2) column.
export const MAX_MONEY_AMOUNT = 1_000_000_000;

// "1,500", "25 000", "1,000,000": one to three digits that do not start with a zero, then groups of exactly
// three. `\s` covers the non-breaking space Word and Excel use as a thousands separator. MoneyAnswer.IsGrouped on
// the server is the same rule (any whitespace, not just the ASCII space) so a pasted "25 000" cannot pass here and
// come back a 422. `\s` and .NET's whitespace differ only in characters the API removes (U+FEFF) or refuses
// (U+0085, U+001C–U+001F) before that rule runs.
const GROUPED_AMOUNT = /^[1-9]\d{0,2}(?:[,\s]\d{3})+$/;

// A comma or a space is a thousands separator or it is refused (BLUM-8955). Every separator used to be stripped
// wherever it sat, so "5,10" — a decimal comma — validated as 510 and, on "Record funding spent", was saved as
// £510. A grouped amount comes back as its digits; anything else comes back as typed, for the caller's pattern
// test to refuse. A typed "£" is dropped here only so callers can tell blank from not-blank: they check for it
// first and refuse it by name.
export const normalizeAmount = (value: string): string => {
  const typed = (value ?? '').replace(/£/g, '').trim();
  return GROUPED_AMOUNT.test(typed) ? typed.replace(/[,\s]/g, '') : typed;
};

// A well-formed amount that happens to carry pence, so the caller can say so specifically instead of falling
// back to "that is not a number". The decimal mark may be a comma as well as a full stop: "5,10" is pence to the
// person who typed it. Mirrors MoneyAnswer.LooksLikePence.
export const isPenceAmount = (normalized: string): boolean => /^(?:\d+|[1-9]\d{0,2}(?:[,\s]\d{3})+)[.,]\d+$/.test(normalized);

// The number a stored amount stands for, for display; null when it is not one. Unlike parseAmount this is not
// deciding what may be saved, so it reads what validation refuses by name — a sign, pence: "-1,500" and "1,500.50"
// are still the numbers they say, and only the pounds are read through normalizeAmount. A misplaced separator is
// not read at all: "5,10" is null, not 510. Mirrors MoneyAnswer.TryRead.
export const readAmount = (value: string): number | null => {
  const typed = (value ?? '').replace(/£/g, '').trim();
  const sign = /^[-+]/.test(typed) ? typed[0] : '';
  const unsigned = typed.slice(sign.length).trimStart();
  const point = unsigned.indexOf('.');
  const pounds = point < 0 ? unsigned : unsigned.slice(0, point);
  const pence = point < 0 ? '' : unsigned.slice(point);
  const plain = `${normalizeAmount(pounds)}${pence}`;
  if (!/^(?:\d+\.?\d*|\.\d+)$/.test(plain)) {
    return null;
  }
  // isFinite guards the digit string long enough to reach Infinity.
  const amount = Number(`${sign}${plain}`);
  return Number.isFinite(amount) ? amount : null;
};

// The numeric value to send to the API: null when empty or invalid (validation blocks invalid before save). A
// typed "£" is stripped here rather than rejected — validation has already refused it, and a draft save must not
// silently persist a different number from the one on screen.
export const parseAmount = (value: string): number | null => {
  const normalized = normalizeAmount(value);
  if (!normalized || !MONEY_PATTERN.test(normalized)) {
    return null;
  }
  return Number(normalized);
};
