// UK financial years, in the "2026/27" shape (start year / two-digit next year) the API stores in
// funding_windows.financial_year and collections.financial_year.
//
// Lived in views/Admin/Collections/display.tsx until BLUM-7608, when organisation allocations needed the same
// options list. Collections still re-exports these so nothing there had to change.

// The financial year containing `date`. The UK government financial year runs 1 April - 31 March, matching the
// backend's Application.Common.Funding.FinancialYearRange.Current and this app's own `reportingPeriod`, which
// bounds a financial year at 1 April and maps Q1 to April-June. The previous implementation here used the
// calendar year, so between 1 January and 31 March it named next year's FY as the current one.
export const currentFinancialYear = (date: Date = new Date()): string => {
  const startYear = date.getMonth() + 1 >= 4 ? date.getFullYear() : date.getFullYear() - 1;
  return formatFinancialYear(startYear);
};

// The financial year after `financialYear`, or an empty string when it is not a well-formed year. The empty
// string is what lets a caller render a label before a year has been chosen without a separate null check.
export const nextFinancialYear = (financialYear: string): string => {
  const startYear = financialYearStartYear(financialYear);
  return startYear === null ? '' : formatFinancialYear(startYear + 1);
};

// Options for a financial-year select: the year before the current one through four years after it.
export const financialYearOptions = (date: Date = new Date()): { value: string; label: string }[] => {
  const startYear = (financialYearStartYear(currentFinancialYear(date)) ?? date.getFullYear()) - 1;
  return Array.from({ length: 6 }, (_unused, index) => {
    const label = formatFinancialYear(startYear + index);
    return { value: label, label };
  });
};

export const financialYearLabel = (financialYear: string | null): string => financialYear || '—';

// The year a financial year ENDS in ("2026/27" → 2027), or null when it is not well-formed. The maturity assessment
// is named by it, mirroring the backend's MaturityAssessmentReference.EndYear.
export const financialYearEndYear = (financialYear: string): number | null => {
  const startYear = financialYearStartYear(financialYear);
  return startYear === null ? null : startYear + 1;
};

const formatFinancialYear = (startYear: number): string => `${startYear}/${((startYear + 1) % 100).toString().padStart(2, '0')}`;

// Mirrors the backend's FinancialYearRange.TryParseStartYear: exactly "YYYY/YY", with an end year that really
// is the start year plus one. A bare Number(split[0]) check would accept the empty string as year 0 and hand
// back "1/02" for it — the shape the loading state of the amend screen passes in.
const financialYearPattern = /^\d{4}\/\d{2}$/;

const financialYearStartYear = (financialYear: string): number | null => {
  // The regex, not Number(), is what rejects "  26/27": Number tolerates leading whitespace.
  if (!financialYearPattern.test(financialYear)) return null;

  const startYear = Number(financialYear.slice(0, 4));
  const endYear = Number(financialYear.slice(5));

  return endYear === (startYear + 1) % 100 ? startYear : null;
};
