const DIACRITICS = /[̀-ͯ]/g;
const NON_ALNUM = /[^a-z0-9]+/g;
const TRIM_DASHES = /^-+|-+$/g;

export const slugifyTestId = (value: string): string =>
  value
    .toLowerCase()
    .normalize('NFKD')
    .replace(DIACRITICS, '')
    .replace(NON_ALNUM, '-')
    .replace(TRIM_DASHES, '');

export const composeTestId = (
  root: string | undefined,
  ...parts: Array<string | number | undefined | null>
): string | undefined => {
  if (!root) return undefined;
  const tail = parts
    .filter((part): part is string | number => part !== undefined && part !== null && part !== '')
    .map((part) => String(part));
  return [root, ...tail].join('-');
};
