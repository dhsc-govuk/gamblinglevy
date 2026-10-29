import { describe, expect, it } from 'vitest';
import { sanitiseDatePart } from './dateParts';

describe('sanitiseDatePart', () => {
  it('keeps digits within the part length', () => {
    expect(sanitiseDatePart('day', '7')).toBe('7');
    expect(sanitiseDatePart('month', '12')).toBe('12');
    expect(sanitiseDatePart('year', '2026')).toBe('2026');
  });

  it('truncates to two digits for day and month and four for year', () => {
    expect(sanitiseDatePart('day', '123')).toBe('12');
    expect(sanitiseDatePart('month', '0312')).toBe('03');
    expect(sanitiseDatePart('year', '202612')).toBe('2026');
  });

  it('strips everything that is not a digit before truncating', () => {
    expect(sanitiseDatePart('month', '3a')).toBe('3');
    expect(sanitiseDatePart('year', ' 2 0 2 6 ')).toBe('2026');
    expect(sanitiseDatePart('month', '03/2026')).toBe('03');
    expect(sanitiseDatePart('day', '-1')).toBe('1');
  });

  it('returns an empty string for an empty or non-numeric value', () => {
    expect(sanitiseDatePart('day', '')).toBe('');
    expect(sanitiseDatePart('year', 'abcd')).toBe('');
  });
});
