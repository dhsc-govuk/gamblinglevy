import React, { useLayoutEffect, useRef } from 'react';
import { ConfirmActionPanel, GovUKButton } from '../../../GovUKComponents';
import { composeTestId } from '../../../../helpers/testIds';

interface Action {
  label: string;
  onClick: () => void;
  testId?: string;
}

interface Props {
  title: string;
  body: React.ReactNode;
  confirmLabel: string;
  onConfirm: () => void;
  cancelLabel: string;
  onCancel: () => void;
  // An optional third choice, for panels where confirming and cancelling are not the only outcomes — e.g.
  // "Leave without saving" beside "Save as draft and exit". It joins the button group next to the confirm
  // button and pushes the cancel link onto its own row beneath, so the two ways of leaving read as a pair.
  secondaryAction?: Action;
  isLoading?: boolean;
  testId?: string;
  // Bumped by the caller to re-announce a panel that is already open. Same prop, and the same reason, as
  // GovUKErrorSummary's: the panel can be re-raised without a single rendered value changing, and mount is
  // then the wrong trigger for moving focus.
  focusTrigger?: number;
}

// The cancel and secondary choices are buttons styled as links because they fire callbacks rather than navigate.
// The button chrome is reset in govuk-refreshed-branding.css, not inline: an inline background would also beat
// the :focus rule that paints the yellow focus fill. Disabled while a save is in flight so leaving cannot race
// a mutation whose onSuccess navigates somewhere else.
const LinkButton = ({ label, onClick, testId, disabled }: Action & { disabled: boolean }): React.ReactElement => (
  <button type="button" className="govuk-link govuk-link--inverse" onClick={onClick} disabled={disabled} data-testid={testId}>
    {label}
  </button>
);

// Callback-driven variant of the blue "Are you sure?" interruption card (Figma 420-29290): white 36px
// heading, 24px lead body, inverse button + inverse link. Wraps the shared ConfirmActionPanel — used
// where confirm/cancel are handlers on the current page (discard changes, delete, restore) rather than
// navigations to a dedicated route.
const ConfirmationPanel = ({
  title,
  body,
  confirmLabel,
  onConfirm,
  cancelLabel,
  onCancel,
  secondaryAction,
  isLoading = false,
  testId,
  focusTrigger,
}: Props): React.ReactElement => {
  // This interruption replaces the page view in place (no route change), so move focus to its heading on
  // mount — otherwise focus lingers on the now-unmounted trigger button and keyboard/screen-reader users
  // are not told the content changed (WCAG 2.4.3). preventScroll: callers scroll the panel into view.
  // focusTrigger repeats that for a caller whose panel can be raised again while it is already open — the
  // panel is not a modal, so the chrome around it stays clickable and the second activation would otherwise
  // leave focus where it was, on a control that just did nothing visible.
  const headingRef = useRef<HTMLHeadingElement>(null);
  useLayoutEffect(() => {
    headingRef.current?.focus({ preventScroll: true });
  }, [focusTrigger]);

  const cancel = <LinkButton label={cancelLabel} onClick={onCancel} testId={composeTestId(testId, 'cancel')} disabled={isLoading} />;

  return (
    <ConfirmActionPanel testId={testId}>
      <h1 ref={headingRef} tabIndex={-1} className="govuk-heading-l" data-testid={composeTestId(testId, 'title')}>
        {title}
      </h1>
      {/* A div, not a <p>: several callers pass multiple <p> elements as the body. The govuk-body-l class
        carries the design's 24px lead size for plain-text bodies; nested paragraphs keep their own class. */}
      <div className="govuk-body-l">{body}</div>
      <div className="govuk-button-group">
        <GovUKButton
          className="govuk-button govuk-button--inverse"
          onClick={onConfirm}
          isLoading={isLoading}
          id={composeTestId(testId, 'confirm')}
          data-testid={composeTestId(testId, 'confirm')}>
          {confirmLabel}
        </GovUKButton>
        {secondaryAction ? (
          <LinkButton label={secondaryAction.label} onClick={secondaryAction.onClick} testId={secondaryAction.testId ?? composeTestId(testId, 'secondary')} disabled={isLoading} />
        ) : (
          cancel
        )}
      </div>
      {secondaryAction && <div className="govuk-button-group">{cancel}</div>}
    </ConfirmActionPanel>
  );
};

export default ConfirmationPanel;
