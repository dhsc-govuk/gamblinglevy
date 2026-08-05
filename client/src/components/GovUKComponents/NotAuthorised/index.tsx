import React, { useEffect, useRef } from 'react';
import { composeTestId } from '../../../helpers/testIds';

interface NotAuthorisedProps {
  goBack: (path: number) => void;
  testId?: string;
}

const NotAuthorised = ({ goBack, testId = 'not-authorised' }: NotAuthorisedProps): React.ReactElement => {
  const mainRef = useRef<HTMLElement>(null);

  // This page can replace a rendered page in place, after an async refusal, with no route change (BLUM-7113) —
  // without moving focus a screen reader announces nothing and the content silently changes underneath it, and a
  // keyboard user's next Tab starts in a page whose landmarks have all been swapped (WCAG 2.4.3 / 4.1.3).
  useEffect(() => {
    mainRef.current?.focus();
  }, []);

  return (
    <div className="govuk-width-container" data-testid={testId}>
      <main
        ref={mainRef}
        tabIndex={-1}
        className="govuk-main-wrapper"
        style={{ textAlign: 'center', minHeight: '50vh', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
        <h1 className="govuk-heading-xl" style={{ marginBottom: '10px' }}>
          403
        </h1>
        <h2 className="govuk-heading-l" data-testid={composeTestId(testId, 'heading')}>
          Not Authorised
        </h2>
        <p className="govuk-body">You do not have permission to access this page.</p>
        <button type="button" className="govuk-button" onClick={() => goBack(-1)} data-testid={composeTestId(testId, 'go-back')}>
          Go Back
        </button>
      </main>
    </div>
  );
};

export default NotAuthorised;
