import React, { useEffect, useRef, useState } from 'react';
import './styles.css';

export interface GovUKTableScrollProps {
  /** What the table lists ("Organisations"). Names the region a screen reader announces when the table is too wide for
   * its column and the box joins the tab order, so make it match the heading the table sits under. */
  label: string;
  /** On the box. No testid is emitted without it. */
  testId?: string;
  /** On the box, for the page's own spacing. */
  className?: string;
  /** The table, as the box's only child: it is the element whose width is watched. */
  children: React.ReactNode;
}

/**
 * The box a table scrolls in when it is wider than its column, so the page itself never scrolls sideways (WCAG 1.4.10,
 * BLUM-9350). GovUKTable renders its table in one; wrap any hand-built `<table className="govuk-table">` that can
 * outgrow its column the same way.
 *
 * While the table overflows, the box is a named region in the tab order (WCAG 2.1.1): a table of plain text has
 * nothing else focusable, Safari never puts a scroll box in the tab order on its own, and arrow keys scroll only the
 * box holding the focus. A table that fits adds no tab stop.
 */
const GovUKTableScroll = ({ label, testId, className, children }: GovUKTableScrollProps): React.ReactElement => {
  const boxRef = useRef<HTMLDivElement>(null);
  const [overflows, setOverflows] = useState(false);
  const [hasFocus, setHasFocus] = useState(false);
  // The one-off window focus listener a background blur leaves behind (below), removed if the box unmounts first.
  const pendingRecheck = useRef<() => void>();
  useEffect(
    () => () => {
      if (pendingRecheck.current) {
        window.removeEventListener('focus', pendingRecheck.current);
      }
    },
    []
  );

  // Watches the table as well as the box, because searching or filtering changes the table's width without resizing
  // the box. Without ResizeObserver (jsdom) the box simply stays out of the tab order. The 1px allowance is rounding:
  // both widths are whole pixels rounded separately, and content columns are fractional (667.5px in ManagementLayout,
  // more so under zoom), so a table that fits can read 1px wider than its box.
  useEffect(() => {
    const box = boxRef.current;
    if (!box || typeof ResizeObserver === 'undefined') {
      return;
    }

    const measure = () => setOverflows(box.scrollWidth - box.clientWidth > 1);
    const observer = new ResizeObserver(measure);
    observer.observe(box);
    if (box.firstElementChild) {
      observer.observe(box.firstElementChild);
    }
    return () => observer.disconnect();
  }, []);

  // Stays a region while it holds the focus, even once the table fits again (the window widened, the user zoomed out):
  // taking the tabindex off the focused element would drop the focus to <body> and lose the user's place. Only the
  // box's own focus counts — focus and blur bubble up from the links and buttons inside it. A blur fired because the
  // browser window lost focus (switching apps or tabs) is not the user leaving the box: it keeps the focus for when the
  // window comes back, so it must keep its tabindex too. Whether it really still has it is settled when the window
  // regains focus, since the focus may have moved on while the window was in the background.
  const isRegion = overflows || hasFocus;
  const trackOwnFocus = (focused: boolean) => (event: React.FocusEvent<HTMLDivElement>) => {
    if (event.target !== event.currentTarget) {
      return;
    }
    if (!focused && !document.hasFocus()) {
      if (pendingRecheck.current) {
        window.removeEventListener('focus', pendingRecheck.current);
      }
      pendingRecheck.current = () => {
        pendingRecheck.current = undefined;
        setHasFocus(document.activeElement === boxRef.current);
      };
      window.addEventListener('focus', pendingRecheck.current, { once: true });
      return;
    }
    setHasFocus(focused);
  };

  return (
    <div
      ref={boxRef}
      className={['app-table-scroll', className].filter(Boolean).join(' ')}
      role={isRegion ? 'region' : undefined}
      aria-label={isRegion ? label : undefined}
      tabIndex={isRegion ? 0 : undefined}
      onFocus={trackOwnFocus(true)}
      onBlur={trackOwnFocus(false)}
      data-testid={testId}>
      {children}
    </div>
  );
};

export default GovUKTableScroll;
