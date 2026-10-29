import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { START_DATE_MESSAGES, startDateErrorParts, validateStartDate } from './display';

// The start date's error is keyed to the input its summary link focuses, and startDateErrorParts reads the message
// back to decide which input to outline. These run every refusal validateStartDate can give through it, so a
// reworded message or a moved key that breaks the outline fails here.
const partsFor = (month: string, year: string) => {
  const refusal = validateStartDate('active', month, year);
  if (!refusal) return null;
  return startDateErrorParts({ [refusal.field]: refusal.message });
};

describe('startDateErrorParts', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(Date.UTC(2026, 9, 14)));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('outlines both inputs when the date is missing', () => {
    expect(validateStartDate('active', '', '')?.message).toBe(START_DATE_MESSAGES.missing);
    expect(partsFor('', '')).toBeUndefined();
  });

  it('outlines only the month when the month is missing', () => {
    expect(partsFor('', '2026')).toEqual(['month']);
  });

  it('outlines only the year when the year is missing or not four digits', () => {
    expect(partsFor('3', '')).toEqual(['year']);
    expect(partsFor('3', '26')).toEqual(['year']);
  });

  it('outlines both inputs when the date is not real or is in the future', () => {
    expect(validateStartDate('active', '13', '2026')?.message).toBe(START_DATE_MESSAGES.notReal);
    expect(partsFor('13', '2026')).toBeUndefined();
    expect(partsFor('3', '1999')).toBeUndefined();
    expect(validateStartDate('active', '11', '2026')?.message).toBe(START_DATE_MESSAGES.inFuture);
    expect(partsFor('11', '2026')).toBeUndefined();
  });

  it('accepts the current month', () => {
    expect(validateStartDate('active', '10', '2026')).toBeNull();
  });

  it('outlines nothing in particular for an error it does not recognise', () => {
    expect(startDateErrorParts({})).toBeUndefined();
    expect(startDateErrorParts({ 'activity-start-month': 'A message from the server' })).toBeUndefined();
  });
});
