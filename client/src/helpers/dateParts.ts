// The parts of a GOV.UK date input (GovUKDateField). Each holds digits only, capped at the part's length, so a
// pasted "03/2026" or a stray letter never reaches the form state.

export type DatePart = 'day' | 'month' | 'year';

export const DATE_PARTS: readonly DatePart[] = ['day', 'month', 'year'];

export const DATE_PART_MAX_LENGTH: Record<DatePart, number> = { day: 2, month: 2, year: 4 };

export const sanitiseDatePart = (part: DatePart, raw: string): string => raw.replace(/\D/g, '').slice(0, DATE_PART_MAX_LENGTH[part]);
