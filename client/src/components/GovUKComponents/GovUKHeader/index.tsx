import React from 'react';
import { GovUKHeaderLogo } from '../../Logos';
import { composeTestId } from '../../../helpers/testIds';

interface GovUKHeaderProps {
  children?: React.ReactNode;
  testId?: string;
}

// The govuk-frontend 6 header is the crown logo only. The v5 serviceName / navigation branches that used to
// live here were removed rather than kept "for later": they named govuk-header__content, __service-name and
// __navigation*, none of which exist in v6's stylesheet, so the first caller to pass either prop would have
// got an unstyled #0000EE link at 1.82:1 on the blue bar — reintroducing from inside GovUKComponents the very
// defect this component was written to fix. v6's answer for a service name and navigation is the separate
// govuk-service-navigation component — build a wrapper for that rather than growing this one back (BLUM-6386).
// No data-module: v6 exports no Header module and this app never calls initAll (see src/index.tsx).
// testId has no default: per the composite-wrapper contract a caller that omits it gets no testids at all,
// rather than a shared literal two headers could both claim.
function GovUKHeader({ children, testId }: GovUKHeaderProps): React.ReactElement {
  return (
    <header className="govuk-header" data-testid={testId}>
      <div className="govuk-header__container govuk-width-container">
        <div className="govuk-header__logo">
          {/* govuk-header__homepage-link, not govuk-header__link--homepage: neither of the latter pair
              exists in govuk-frontend 6, so the anchor matched no rule at all and fell back to the UA's
              #0000EE — the crown logotype inherits it via fill="currentcolor", rendering at ~1.8:1 on the
              blue bar (SC 1.4.11 wants 3:1) with only Chrome's 1px ring on focus instead of the mandated
              #ffdd00 (SC 2.4.7). govuk-link--inverse is what lets govuk-frontend's own :link/:focus rules
              through: the app-wide a:not(…):not(.govuk-link--inverse):focus rule in
              styles/govuk-refreshed-branding.css would otherwise repaint it. The same pair is on the crown
              link of the app's own bar, layouts/General/LayoutHeader/HeaderBar, which LayoutHeader, Cookies and
              the Accessibility statement render instead of this component: LayoutHeader has sibling content
              this logo-only shape could not hold, and .govuk-header__logo's 16px/12px padding would change the
              bar's height on all three. */}
          <a href="https://www.gov.uk" className="govuk-header__homepage-link govuk-link--inverse" data-testid={composeTestId(testId, 'home-link')}>
            <GovUKHeaderLogo />
          </a>
        </div>
        {children}
      </div>
    </header>
  );
}

export default GovUKHeader;
