import React, { useEffect, useState } from 'react';
import './styles.css';

interface PageLoadingProps {
  label?: string;
  testId?: string;
  fullViewport?: boolean;
  /**
   * Wait this many milliseconds before rendering the spinner. Useful when this
   * loader sits in front of a page that has its own quick-flash loader — set
   * a delay so fast resolves never paint the main loader at all, and slow
   * resolves still get covered cleanly. 0 = render immediately.
   */
  delayMs?: number;
}

/**
 * Full-area loading screen used while page-level data (or auth bootstrap) is in flight.
 *
 * - Defaults to filling at least 50vh so it doesn't collapse onto a single line.
 * - Pass `fullViewport` when there's no layout chrome around it (e.g. the
 *   pre-auth window before MSAL + profile resolves) and you want it centred on
 *   the whole screen.
 * - Announces "Loading" via `role="status"` + `aria-live="polite"` so assistive
 *   tech surfaces the wait without spamming updates.
 */
const PageLoading = ({ label = 'Loading', testId = 'page-loading', fullViewport = false, delayMs = 0 }: PageLoadingProps): React.ReactElement | null => {
  const [visible, setVisible] = useState(delayMs === 0);

  useEffect(() => {
    if (delayMs === 0) return;
    const timer = window.setTimeout(() => setVisible(true), delayMs);
    return () => window.clearTimeout(timer);
  }, [delayMs]);

  if (!visible) return null;

  return (
    <div className={`page-loading${fullViewport ? ' page-loading--full' : ''}`} role="status" aria-live="polite" data-testid={testId}>
      <span className="page-loading__spinner" aria-hidden="true" />
      <span className="govuk-visually-hidden">{label}</span>
    </div>
  );
};

export default PageLoading;
