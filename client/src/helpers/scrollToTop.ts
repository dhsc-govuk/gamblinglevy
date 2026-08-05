// Instant jump to the top of the page. Used on route changes and in-page step transitions so a user
// who scrolled down does not land at the bottom of the next view. Instant (not smooth) — navigations
// should not animate. For the validation error-summary scroll see focusErrorSummary.ts (smooth).
const scrollToTop = (): void => {
  if (typeof window === 'undefined') return;

  window.scrollTo(0, 0);
};

export default scrollToTop;
