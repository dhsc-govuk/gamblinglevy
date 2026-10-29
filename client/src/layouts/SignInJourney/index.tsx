import React from 'react';
import { GovUKHeader, GovUKSkipLink } from '../../components/GovUKComponents';
import { composeTestId } from '../../helpers/testIds';
import { SERVICE_NAME } from '../../hooks/usePageTitle';
import LayoutFooter from '../General/LayoutFooter';
import './styles.css';

interface Props {
  /** Root testid; the header's derives from it unless headerTestId is given. */
  testId: string;
  /**
   * For a page whose root testid changes with its state (loading, not enabled, …) while its header should keep
   * one stable selector across those states.
   */
  headerTestId?: string;
  children: React.ReactNode;
}

/**
 * The chrome of the sign-in journey before a role is known: the sign-in page and the three MFA pages. GOV.UK
 * header, the service-name banner, `main#main-content` and the footer — no role navigation or breadcrumbs, which
 * GeneralLayout would need a role claim to draw.
 *
 * Children render inside the width container, so each page places its own back link and govuk-main-wrapper:
 * the sign-in page puts its back link outside the wrapper, the MFA pages inside it.
 *
 * Extracted from four copies (SignIn, Setup, Verify and an unused styles.ts) that had already drifted: Verify's
 * MUI header linked the crown to the app instead of gov.uk and sat at a different height (BLUM-8758).
 */
const SignInJourneyLayout = ({ testId, headerTestId, children }: Props): React.ReactElement => (
  <div className="sign-in-journey" data-testid={testId}>
    <GovUKSkipLink />

    <GovUKHeader testId={headerTestId ?? composeTestId(testId, 'header')} />

    <section className="sign-in-journey__service-banner" aria-label="Service information">
      <div className="govuk-width-container">
        <span className="sign-in-journey__service-name">{SERVICE_NAME}</span>
      </div>
    </section>

    <main className="sign-in-journey__main" id="main-content" role="main">
      <div className="govuk-width-container">{children}</div>
    </main>

    <LayoutFooter />
  </div>
);

export default SignInJourneyLayout;
