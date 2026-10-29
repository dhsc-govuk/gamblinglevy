import React, { useEffect, useRef } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { GovUKButton, H1, Paragraph } from '../../../GovUKComponents';
// One definition of this GDS page wording for the whole service — two literals for the same user-facing string is
// the drift the constant exists to prevent. Deep-imported, not via the barrel: that barrel re-exports four user
// views and the users API, none of which this component has any business pulling in (the useOrganisationUser
// precedent imports the same constant the same way).
import { SERVICE_PROBLEM_HEADING } from '../../OrganisationUsers/UserLoadFailed';
import { composeTestId } from '../../../../helpers/testIds';

export interface ReportLoadFailureProps {
  // 'forbidden' is deliberately absent: a refusal renders NotAuthorised, which replaces the layout because it
  // carries its own <main>. This component always renders inside the layout, so the breadcrumbs and the header
  // navigation survive and there is always a way forward.
  kind: 'not-found' | 'failed';
  notFoundHeading: string;
  message: string;
  returnTo: string;
  returnLabel: string;
  onRetry: () => void;
  testId: string;
}

// The two in-layout failure pages for the DHSC Reports area. They are one component because the only real decision
// is which of them to show, and that decision used to be absent: a single `if (!data)` branch rendered the
// not-found page for a 500 as well, so an outage was reported to admins as missing data (BLUM-7216).
//
// Focus moves to the wrapper on mount rather than announcing through a live region, matching NotAuthorised,
// NotFound and UserLoadFailed: all four are the same event — an async response replacing the page with no route
// change — and one mechanism is one thing to verify. Focus also relocates the user, which role="alert" does not.
// The mount-only effect covers the retry path too: react-query resets a query with no cached data back to
// `pending` on refetch, so this component unmounts to PageLoading and remounts on each failure.
// The ref sits on the wrapper because the shared H1 is a plain function component, not a forwardRef.
const ReportLoadFailure = ({ kind, notFoundHeading, message, returnTo, returnLabel, onRetry, testId }: ReportLoadFailureProps): React.ReactElement => {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    containerRef.current?.focus();
  }, []);

  const isNotFound = kind === 'not-found';

  // The two states look different and mean different things, so they get different roots — otherwise the only way
  // to tell them apart in a test is to assert on heading text, which the testid contract steers away from.
  const rootTestId = composeTestId(testId, kind);

  return (
    <div ref={containerRef} tabIndex={-1} data-testid={rootTestId}>
      <H1 data-testid={composeTestId(rootTestId, 'heading')}>{isNotFound ? notFoundHeading : SERVICE_PROBLEM_HEADING}</H1>
      <Paragraph data-testid={composeTestId(rootTestId, 'message')}>{message}</Paragraph>
      <div className="govuk-button-group">
        {/* Retrying a 404 cannot change the answer, so the button is offered only for a fault. */}
        {!isNotFound && (
          <GovUKButton onClick={onRetry} data-testid={composeTestId(rootTestId, 'retry')}>
            Try again
          </GovUKButton>
        )}
        <RouterLink to={returnTo} className="govuk-link" data-testid={composeTestId(rootTestId, 'return')}>
          {returnLabel}
        </RouterLink>
      </div>
    </div>
  );
};

export default ReportLoadFailure;
