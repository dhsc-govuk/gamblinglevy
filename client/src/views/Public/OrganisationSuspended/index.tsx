import React, { useState } from 'react';
import usePageTitle from '../../../hooks/usePageTitle';
import { useAuthProvider } from '../../../components/AuthProvider';
import { GovUKHeaderLogo } from '../../../components/Logos';
import { GovUKButton, GovUKSkipLink, H1, Paragraph } from '../../../components/GovUKComponents';
import LayoutFooter from '../../../layouts/General/LayoutFooter';

const OrganisationSuspended = (): React.ReactElement => {
  usePageTitle('Your organisation’s access has been suspended');
  const { signOut } = useAuthProvider();
  const [signingOut, setSigningOut] = useState(false);

  const handleSignOut = () => {
    setSigningOut(true);
    signOut();
  };

  return (
    <div data-testid="public-organisation-suspended">
      <GovUKSkipLink />

      <header className="govuk-header" data-module="govuk-header">
        <div className="govuk-header__container govuk-width-container">
          <div className="govuk-header__logo">
            <a href="https://www.gov.uk" className="govuk-header__link govuk-header__link--homepage">
              <GovUKHeaderLogo />
            </a>
          </div>
        </div>
      </header>

      <main className="govuk-width-container" id="main-content" role="main">
        <div className="govuk-main-wrapper">
          <div className="govuk-grid-row">
            <div className="govuk-grid-column-two-thirds">
              <H1>Your organisation’s access has been suspended</H1>
              <Paragraph>
                Your organisation’s access to Share progress preventing gambling harms has been suspended by DHSC. You cannot sign in to the service while your organisation is
                suspended.
              </Paragraph>
              <Paragraph>If you think this is a mistake, contact DHSC.</Paragraph>
              <GovUKButton onClick={handleSignOut} isLoading={signingOut} data-testid="public-organisation-suspended-sign-out">
                Sign out
              </GovUKButton>
            </div>
          </div>
        </div>
      </main>

      <LayoutFooter />
    </div>
  );
};

export default OrganisationSuspended;
