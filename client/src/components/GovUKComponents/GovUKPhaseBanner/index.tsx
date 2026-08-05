import React from 'react';
import { composeTestId } from '../../../helpers/testIds';

interface Props {
  phase?: 'alpha' | 'beta';
  feedbackUrl: string;
  testId?: string;
}

const GovUKPhaseBanner = ({ phase = 'beta', feedbackUrl, testId = 'phase-banner' }: Props): React.ReactElement => {
  const phaseLabel = phase.charAt(0).toUpperCase() + phase.slice(1);

  return (
    <div className="govuk-phase-banner" data-testid={testId}>
      <p className="govuk-phase-banner__content">
        <strong className="govuk-tag govuk-phase-banner__content__tag">{phaseLabel}</strong>
        <span className="govuk-phase-banner__text">
          This is a new service – your{' '}
          <a className="govuk-link" href={feedbackUrl} target="_blank" rel="noopener noreferrer" data-testid={composeTestId(testId, 'feedback-link')}>
            feedback (opens in new tab)
          </a>{' '}
          will help us to improve it.
        </span>
      </p>
    </div>
  );
};

export default GovUKPhaseBanner;
