import type { MaturityPageDto, MaturityPageVisibilityDto } from './queries';

// Page-level visibility (BLUM-7855): a whole page is hidden unless its rule's question holds one of the listed
// values. Mirrors the backend MaturityAnswerValidator.IsPageVisible — the two must agree, or the client would step
// the user onto a page the server then refuses to require, or skip one it insists on.
export const isPageVisible = (rule: MaturityPageVisibilityDto | null, answers: Record<string, string | undefined>): boolean => {
  if (!rule) {
    return true;
  }
  const value = answers[rule.questionCode];
  if (!value) {
    return false;
  }
  return value
    .split(',')
    .map((v) => v.trim())
    .some((v) => rule.values.includes(v));
};

export const visiblePages = (pages: MaturityPageDto[], answers: Record<string, string | undefined>): MaturityPageDto[] =>
  pages.filter((page) => isPageVisible(page.visibleWhen, answers));

export const firstPageCode = (pages: MaturityPageDto[], answers: Record<string, string | undefined>): string | undefined => visiblePages(pages, answers)[0]?.code;

// The next / previous VISIBLE page after the current one — a hidden page is stepped over in both directions.
// Undefined past either end (Continue then goes to the section check; Back to the task list).
export const nextPageCode = (pages: MaturityPageDto[], answers: Record<string, string | undefined>, currentPageCode: string): string | undefined => {
  const visible = visiblePages(pages, answers);
  const index = visible.findIndex((page) => page.code === currentPageCode);
  return index >= 0 ? visible[index + 1]?.code : undefined;
};

export const previousPageCode = (pages: MaturityPageDto[], answers: Record<string, string | undefined>, currentPageCode: string): string | undefined => {
  const visible = visiblePages(pages, answers);
  const index = visible.findIndex((page) => page.code === currentPageCode);
  return index > 0 ? visible[index - 1].code : undefined;
};

// The page a question sits on — how a 422 from the section check links back to the page that owns the error.
export const pageOwning = (pages: MaturityPageDto[], questionCode: string): string | undefined =>
  pages.find((page) => page.questions.some((question) => question.code === questionCode))?.code;
