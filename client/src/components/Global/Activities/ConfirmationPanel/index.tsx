import React, { useLayoutEffect, useRef } from 'react';
import { ConfirmActionPanel, GovUKButton } from '../../../GovUKComponents';
import { composeTestId } from '../../../../helpers/testIds';

interface Props {
  title: string;
  body: React.ReactNode;
  confirmLabel: string;
  onConfirm: () => void;
  cancelLabel: string;
  onCancel: () => void;
  isLoading?: boolean;
  testId?: string;
}

// Callback-driven variant of the blue "Are you sure?" interruption card (Figma 420-29290): white 36px
// heading, 24px lead body, inverse button + inverse link. Wraps the shared ConfirmActionPanel — used
// where confirm/cancel are handlers on the current page (discard changes, delete, restore) rather than
// navigations to a dedicated route. The cancel is a button styled as a link because it fires a callback.
const ConfirmationPanel = ({ title, body, confirmLabel, onConfirm, cancelLabel, onCancel, isLoading = false, testId }: Props): React.ReactElement => {
  // This interruption replaces the page view in place (no route change), so move focus to its heading on
  // mount — otherwise focus lingers on the now-unmounted trigger button and keyboard/screen-reader users
  // are not told the content changed (WCAG 2.4.3). preventScroll: callers scroll the panel into view.
  const headingRef = useRef<HTMLHeadingElement>(null);
  useLayoutEffect(() => {
    headingRef.current?.focus({ preventScroll: true });
  }, []);

  return (
    <ConfirmActionPanel testId={testId}>
      <h1 ref={headingRef} tabIndex={-1} className="govuk-heading-l" data-testid={composeTestId(testId, 'title')}>
        {title}
      </h1>
      {/* A div, not a <p>: several callers pass multiple <p> elements as the body. The govuk-body-l class
        carries the design's 24px lead size for plain-text bodies; nested paragraphs keep their own class. */}
      <div className="govuk-body-l">{body}</div>
      <div className="govuk-button-group">
        <GovUKButton className="govuk-button govuk-button--inverse" onClick={onConfirm} isLoading={isLoading} data-testid={composeTestId(testId, 'confirm')}>
          {confirmLabel}
        </GovUKButton>
        <button
          type="button"
          className="govuk-link govuk-link--inverse"
          style={{ background: 'none', border: 0, padding: 0, cursor: 'pointer' }}
          onClick={onCancel}
          data-testid={composeTestId(testId, 'cancel')}>
          {cancelLabel}
        </button>
      </div>
    </ConfirmActionPanel>
  );
};

export default ConfirmationPanel;
