import React, { useEffect, useRef } from 'react';
import { composeTestId } from '../../../helpers/testIds';

export interface GovUKErrorSummaryItem {
  targetName: string;
  text: string;
}

interface GovUKErrorSummaryProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children'> {
  heading?: string;
  errors: GovUKErrorSummaryItem[];
  // Bump this (a submit-attempt counter) to move DOM focus onto the summary. Each bump is delivered exactly once,
  // the first time the summary has errors to show: straight away when they are set along with it, later when a
  // refusal arrives after it — the bump stays pending while the summary is empty. Mounting never focuses,
  // whatever the value. So ALWAYS render the summary and hide it with `errors={[]}`: one that only mounts
  // together with its error has no earlier trigger value to compare with, and misses the bump. ESLint rejects
  // `cond && <ErrorSummary …/>` for that reason.
  //
  // Two things a form has to keep true. A submit handler that leaves the refusal to a mutation's `onError` must
  // clear or replace its errors in the same update as the bump (or bump again in `onError`) — otherwise the bump
  // is spent on the errors still showing and the refusal arrives unfocused. And any change of the value counts,
  // including a reset to 0, so only ever count upwards.
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
// error text as an onClick-only span with no `href` and no `role="alert"`. `focusTrigger` is the only way
// focus reaches a summary (BLUM-8760): each one focuses itself through its own ref, so two mounted at once
// cannot take each other's focus.
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

  // The trigger value this instance has already acted on. Starting from the value it mounted with is what keeps a
  // remount silent — a loading gate reopening, a page coming back with its errors restored.
  const handledTrigger = useRef(focusTrigger);
  const hasErrors = errors.length > 0;

  // Runs on a bump and when errors appear or go — never on a change within a non-empty list, so a form that clears
  // one field's error per keystroke (Collections) does not have focus pulled out of the field being typed in.
  useEffect(() => {
    // Null while there are no errors: the root below is not rendered. A bump made then is left unhandled, so this
    // runs again when errors arrive (hasErrors) and delivers it.
    const summary = ref.current;
    if (!summary || focusTrigger === handledTrigger.current) {
      return;
    }
    handledTrigger.current = focusTrigger;
    const prefersReducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    // preventScroll so the browser's instant focus-scroll does not fight the smooth scroll.
    summary.focus({ preventScroll: true });
    summary.scrollIntoView({ behavior: prefersReducedMotion ? 'auto' : 'smooth', block: 'start' });
  }, [focusTrigger, hasErrors]);

  if (errors.length === 0) {
    return null;
  }

  return (
    <div {...rootProps} className={['govuk-error-summary', className].filter(Boolean).join(' ')} data-module="govuk-error-summary" tabIndex={-1} ref={ref} data-testid={dataTestId}>
      <div role="alert">
        <h2 className="govuk-error-summary__title">{heading}</h2>
        <div className="govuk-error-summary__body">
          <ul className="govuk-list govuk-error-summary__list">
            {errors.map((error) => (
              <li key={error.targetName}>
                <a
                  href={`#${error.targetName}`}
                  data-testid={composeTestId(dataTestId, 'link', error.targetName)}
                  onClick={(event) => handleErrorLinkClick(event, error.targetName)}>
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
