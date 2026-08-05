// Moves keyboard/screen-reader focus to the error summary and scrolls it into view (GDS: the summary
// must receive focus when it appears). The summary root carries a stable data-error-summary marker
// (GovUKComponents/GovUKErrorSummary) and govuk-react's root is already tabIndex={-1} with a visible
// focus style. Focus runs with preventScroll so the browser's native focus-scroll doesn't fight the
// smooth scroll. Falls back to a plain scroll-to-top when no summary is mounted yet (e.g. the effect
// fired while the page was still behind a loading gate).
const focusErrorSummary = (): void => {
  if (typeof window === 'undefined') return;

  const prefersReducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const behavior: ScrollBehavior = prefersReducedMotion ? 'auto' : 'smooth';

  const summary = document.querySelector<HTMLElement>('[data-error-summary]');
  if (summary) {
    summary.focus({ preventScroll: true });
    summary.scrollIntoView({ behavior, block: 'start' });
    return;
  }

  window.scrollTo({ top: 0, behavior });
};

export default focusErrorSummary;
