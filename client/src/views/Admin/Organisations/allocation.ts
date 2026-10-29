// Annual-allocation input handling, shared by the create and amend screens.
//
// Both screens ask for the same money value, but until BLUM-7608 only the create screen masked thousands
// separators as you type and only it rejected more than two decimal places — the amend screen accepted both
// and let the API answer. Extracted here so the two cannot drift again.

// A complete, valid amount: digits with at most two decimal places, commas already stripped.
export const annualAllocationPattern = /^\d+(?:\.\d{0,2})?$/;

// A partial amount that is still being typed — the empty string and a trailing "." are both allowed here
// and rejected by annualAllocationPattern on submit.
export const annualAllocationInputPattern = /^\d*(?:\.\d{0,2})?$/;

export const removeAnnualAllocationFormatting = (value: string): string => value.replace(/,/g, '');

export const formatAnnualAllocation = (value: string): string => {
  const [wholePart, decimalPart] = value.split('.');
  const formattedWholePart = wholePart.replace(/\B(?=(\d{3})+(?!\d))/g, ',');

  return decimalPart === undefined ? formattedWholePart : `${formattedWholePart}.${decimalPart}`;
};

// The keystroke handler shared by every allocation input: ignores anything that could not become a valid
// amount, and re-inserts thousands separators into what remains.
export const handleAnnualAllocationInput = (value: string, setValue: (next: string) => void): void => {
  const nextValue = removeAnnualAllocationFormatting(value);
  if (annualAllocationInputPattern.test(nextValue)) {
    setValue(formatAnnualAllocation(nextValue));
  }
};

// Validates one allocation amount. Returns an error message, or undefined when the value is acceptable.
// `required` is false for the upcoming year's allocation, which DHSC may not know yet (BLUM-7608).
export const validateAnnualAllocation = (value: string, label: string, required: boolean): string | undefined => {
  const trimmed = removeAnnualAllocationFormatting(value.trim());

  if (!trimmed) {
    return required ? `Enter the ${label}` : undefined;
  }

  if (!annualAllocationPattern.test(trimmed)) {
    return `${label.charAt(0).toUpperCase()}${label.slice(1)} must be a number with no more than 2 decimal places`;
  }

  if (Number.isNaN(Number(trimmed)) || Number(trimmed) <= 0) {
    return `${label.charAt(0).toUpperCase()}${label.slice(1)} must be a number greater than 0`;
  }

  return undefined;
};
