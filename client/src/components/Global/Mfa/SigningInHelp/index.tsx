import React from 'react';
import { GovUKDetails } from '../../../GovUKComponents';
import { GUIDANCE_SUPPORT_EMAIL, GUIDANCE_SUPPORT_MAILTO, GUIDANCE_SUPPORT_RESPONSE_TIME } from '../../../../helpers/constants';
import { composeTestId } from '../../../../helpers/testIds';

interface Props {
  // The details root is `{testId}-details` and the email link `{testId}-email`, so `mfa-verify-help` keeps the
  // testids the pages had before this was shared.
  testId: string;
}

// The "Help with signing in" panel under the security-code field, shared by views/Mfa/Verify and step 2 of
// views/Mfa/Setup, which show the same screen (BLUM-9016). Verify is in the entry bundle, so this imports no view.
const SigningInHelp = ({ testId }: Props): React.ReactElement => (
  <GovUKDetails summary="Help with signing in" data-testid={composeTestId(testId, 'details')}>
    <p className="govuk-body">If you have problems signing in or getting a code, contact the DHSC support team.</p>
    <p className="govuk-body">
      Email:{' '}
      <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid={composeTestId(testId, 'email')}>
        {GUIDANCE_SUPPORT_EMAIL}
      </a>
    </p>
    <p className="govuk-body">{GUIDANCE_SUPPORT_RESPONSE_TIME}</p>
  </GovUKDetails>
);

export default SigningInHelp;
