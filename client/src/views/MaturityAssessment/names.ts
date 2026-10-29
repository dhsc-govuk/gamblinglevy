import { formatGovUkDate } from '../Admin/Collections/display';

// The assessment's display name and period, in a leaf module: views/Reports/display.tsx names maturity rows on
// the shared LA history with these too, and importing them from ./display would make the two display modules
// import each other.

// "Maturity assessment for 2027" — named by the year the financial year ENDS in (Figma 2744-182185), which is the
// same year the reference number starts with.
export const maturityAssessmentName = (financialYearEndYear: number | null | undefined): string =>
  financialYearEndYear ? `Maturity assessment for ${financialYearEndYear}` : 'Maturity assessment';

// "1 April 2026 to 31 March 2027" — the whole financial year, so no "Quarter n" line.
export const maturityPeriodValue = (start: string | null, end: string | null): string => `${formatGovUkDate(start)} to ${formatGovUkDate(end)}`;
