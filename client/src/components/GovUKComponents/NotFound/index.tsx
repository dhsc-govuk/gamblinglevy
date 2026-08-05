import React, { useEffect, useRef } from 'react';
import { composeTestId } from '../../../helpers/testIds';

interface NotFoundProps {
  goBack: (path: number) => void;
  testId?: string;
}

const NotFound = ({ goBack, testId = 'not-found' }: NotFoundProps): React.ReactElement => {
  const mainRef = useRef<HTMLElement>(null);

  // Like NotAuthorised, this can replace a rendered page in place once a lookup resolves, with no route change —
  // without moving focus the swap is silent to a screen reader (WCAG 2.4.3 / 4.1.3).
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
          404
        </h1>
        <h2 className="govuk-heading-l" data-testid={composeTestId(testId, 'heading')}>
          Page Not Found
        </h2>
        <p className="govuk-body">The page you are looking for does not exist.</p>
        <button type="button" className="govuk-button" onClick={() => goBack(-1)} data-testid={composeTestId(testId, 'go-back')}>
          Go Back
        </button>
      </main>
    </div>
  );
};

export default NotFound;
