import React, { useEffect, useRef } from 'react';
import { composeTestId } from '../../../helpers/testIds';

export interface GovUKErrorSummaryItem {
  targetName: string;
  text: string;
}

interface GovUKErrorSummaryProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children'> {
  heading?: string;
  errors: GovUKErrorSummaryItem[];
  // Bump this (e.g. a submit-attempt counter) to move DOM focus onto the summary again — needed because the
  // same error set can be re-shown after a repeat submit without `errors` itself changing.
  focusTrigger?: number;
  onHandleErrorClick?: (targetName: string) => void;
  testId?: string;
  'data-testid'?: string;
}

// GOV.UK error summary built to the exact markup required by the Design System
// (https://design-system.service.gov.uk/components/error-summary/): `role="alert"` on the inner wrapper so
// screen readers announce it immediately, and a real `<a href="#id">` per error so keyboard and screen-reader
// users can jump straight to the offending field. `focusTrigger` moves real DOM focus onto the summary, per
// the spec's "move keyboard focus to the error summary" requirement.
//
// This replaces govuk-react's `ErrorSummary` (re-exported as `ErrorSummary` from this index), which renders
// error text as an onClick-only span with no `href` and no `role="alert"`. The root keeps a stable
// `data-error-summary` marker for the legacy focusErrorSummary helper used by older forms.
const GovUKErrorSummary = ({
  heading = 'There is a problem',
  errors,
  focusTrigger,
  onHandleErrorClick,
  testId,
  className,
  ...rootProps
}: GovUKErrorSummaryProps): React.ReactElement | null => {
  const ref = useRef<HTMLDivElement>(null);
  const dataTestId = testId ?? rootProps['data-testid'];

  const scrollTargetFor = (target: HTMLElement): Element => {
    const radioConditional = target.closest('.govuk-radios__conditional');
    if (radioConditional?.previousElementSibling?.classList.contains('govuk-radios__item')) {
      return radioConditional.previousElementSibling;
    }

    const checkboxConditional = target.closest('.govuk-checkboxes__conditional');
    if (checkboxConditional?.previousElementSibling?.classList.contains('govuk-checkboxes__item')) {
      return checkboxConditional.previousElementSibling;
    }

    return target.closest('.govuk-form-group--error') ?? target.closest('.govuk-form-group') ?? target;
  };

  const handleErrorLinkClick = (event: React.MouseEvent<HTMLAnchorElement>, targetName: string) => {
    event.preventDefault();

    if (onHandleErrorClick) {
      onHandleErrorClick(targetName);
      return;
    }

    const target = document.getElementById(targetName);
    if (!target) {
      return;
    }

    const scrollTarget = scrollTargetFor(target);
    scrollTarget.scrollIntoView({ block: 'start' });

    if (target instanceof HTMLElement) {
      target.focus({ preventScroll: true });
    }
  };

  useEffect(() => {
    if (!focusTrigger || errors.length === 0) {
      return;
    }
    ref.current?.focus();
    // Only re-focus when focusTrigger changes (e.g. a new submit attempt) — not on every `errors` update.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusTrigger]);

  if (errors.length === 0) {
    return null;
  }

  return (
    <div
      {...rootProps}
      className={['govuk-error-summary', className].filter(Boolean).join(' ')}
      data-error-summary=""
      data-module="govuk-error-summary"
      tabIndex={-1}
      ref={ref}
      data-testid={dataTestId}>
      <div role="alert">
        <h2 className="govuk-error-summary__title">{heading}</h2>
        <div className="govuk-error-summary__body">
          <ul className="govuk-list govuk-error-summary__list">
            {errors.map((error) => (
              <li key={error.targetName}>
                <a href={`#${error.targetName}`} data-testid={composeTestId(dataTestId, 'link', error.targetName)} onClick={(event) => handleErrorLinkClick(event, error.targetName)}>
                  {error.text}
                </a>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
};

export default GovUKErrorSummary;
