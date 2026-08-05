// Shared GBP formatter. Renders an em dash for null amounts; otherwise a £ value with up to two
// decimals and en-GB thousands separators. Single source of truth for money display across the app.
export const formatGBP = (amount: number | null): string => (amount === null ? '—' : `£${amount.toLocaleString('en-GB', { maximumFractionDigits: 2 })}`);
