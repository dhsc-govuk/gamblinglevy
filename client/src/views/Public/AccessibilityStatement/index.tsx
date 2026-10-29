import React from 'react';
import usePageTitle from '../../../hooks/usePageTitle';
import { GovUKHeaderLogo } from '../../../components/Logos';
import { GovUKBreadcrumbs, GovUKNotificationDisplay, GovUKSkipLink, H1, H2, H3, ListItem, Paragraph, UnorderedList } from '../../../components/GovUKComponents';
import { homeLinkForRole, useAuthProvider } from '../../../components/AuthProvider';
import HeaderBar from '../../../layouts/General/LayoutHeader/HeaderBar';
import LayoutFooter from '../../../layouts/General/LayoutFooter';
// Shared with the guidance pages, but pinned here by BLUM-7684 AC3: this value is published copy in a
// statutory accessibility statement, so it must keep matching the signed-off document. If the service's
// contact address ever changes, the statement has to be re-agreed with DHSC — not just re-pointed.
import { GUIDANCE_SUPPORT_EMAIL, GUIDANCE_SUPPORT_MAILTO } from '../../../helpers/constants';

const baseUrl = import.meta.env.BASE_URL || '/';

// Every string on this page is the copy DHSC signed off in the accessibility statement draft attached to
// BLUM-7684, reproduced word for word — AC3 is an exact-match criterion, so reword nothing here (including
// the title-cased service name, which deliberately differs from the sentence-case SERVICE_NAME used for the
// page title and everywhere else in the app).
//
// SERVICE_URL is the address the published statement names. It is hardcoded rather than derived from the
// router because the statement has to point at the service's public home (the DAC deployment), not at
// whichever environment happens to be serving this page.
const SERVICE_URL = 'https://analytics.dhsc.gov.uk/share-progress-preventing-gambling-harms/sign-in';
const ABILITYNET_URL = 'https://mcmw.abilitynet.org.uk/';
const EASS_URL = 'https://www.equalityadvisoryservice.com/';
const WCAG_URL = 'https://www.w3.org/TR/WCAG22/';

const AccessibilityStatement = (): React.ReactElement => {
  const { userRole } = useAuthProvider();

  usePageTitle('Accessibility statement');

  return (
    <div data-testid="public-accessibility-statement">
      <GovUKSkipLink />

      {/* The app's blue header bar, but without the sign-out link or the service-navigation bar — this is a
          public informational page, not an app surface. */}
      <header role="banner" data-testid="public-accessibility-statement-header">
        <HeaderBar>
          {/* Classes and the absence of an inline colour are load-bearing — see the crown link in
                layouts/General/LayoutHeader for the full reasoning (BLUM-6386). */}
          <a href={baseUrl} className="govuk-header__homepage-link govuk-link--inverse" data-testid="public-accessibility-statement-header-home-link">
            <GovUKHeaderLogo />
          </a>
        </HeaderBar>
      </header>

      <main className="govuk-main-wrapper" id="main-content" role="main">
        <div className="govuk-width-container">
          <GovUKBreadcrumbs items={[{ label: 'Home', href: homeLinkForRole(userRole) }]} />

          <div className="govuk-grid-row">
            <div className="govuk-grid-column-two-thirds">
              {/* The app-wide notification surface. These two public pages build their own chrome rather than
                  rendering GeneralLayout, so they do not get the GovUKNotificationDisplay in layouts/General/Content
                  — and they became reachable mid-flow when BLUM-7831 started intercepting the footer's internal
                  links. Without this mount, a draft saved on the way here shows no confirmation at all and the
                  notification then flashes on the next page that does render the display. */}
              <GovUKNotificationDisplay />

              <H1>Accessibility Statement for Share Progress Preventing Gambling Harms</H1>

              <Paragraph>
                This accessibility statement applies to the{' '}
                <a className="govuk-link" href={SERVICE_URL} data-testid="public-accessibility-statement-link-service">
                  Share Progress Preventing Gambling Harms service
                </a>
                .
              </Paragraph>
              <Paragraph>We want as many people as possible to be able to use this website. For example, that means you should be able to:</Paragraph>
              <UnorderedList>
                <ListItem>change colours, contrast levels and fonts</ListItem>
                <ListItem>zoom in up to 400% without the text spilling off the screen</ListItem>
                <ListItem>navigate most of the website using just a keyboard</ListItem>
                <ListItem>navigate most of the website using speech recognition software</ListItem>
                <ListItem>listen to most of the website using a screen reader (including the most recent versions of JAWS, NVDA and VoiceOver)</ListItem>
              </UnorderedList>
              <Paragraph>We’ve also made the website text as simple as possible to understand.</Paragraph>
              <Paragraph>
                <a className="govuk-link" href={ABILITYNET_URL} data-testid="public-accessibility-statement-link-abilitynet">
                  AbilityNet
                </a>{' '}
                has advice on making your device easier to use if you have a disability.
              </Paragraph>

              <H2>How accessible this website is</H2>
              <Paragraph>We know some parts of this website are not fully accessible:</Paragraph>
              <UnorderedList>
                <ListItem>text within dropdowns does not always update when text spacing or custom styling is applied.</ListItem>
              </UnorderedList>

              <H2>Feedback and contact information</H2>
              <Paragraph>
                If you find any problems not listed on this page or think we’re not meeting accessibility requirements, contact:{' '}
                <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="public-accessibility-statement-link-email-feedback">
                  {GUIDANCE_SUPPORT_EMAIL}
                </a>
                .
              </Paragraph>
              <Paragraph>
                If you need information on this website in a different format like accessible PDF, large print, easy read, audio recording or braille, please contact:{' '}
                <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="public-accessibility-statement-link-email-formats">
                  {GUIDANCE_SUPPORT_EMAIL}
                </a>
                .
              </Paragraph>

              <H2>Enforcement procedure</H2>
              <Paragraph>
                The Equality and Human Rights Commission (EHRC) is responsible for enforcing the Public Sector Bodies (Websites and Mobile Applications) (No. 2) Accessibility
                Regulations 2018 (the ‘accessibility regulations’). If you’re not happy with how we respond to your complaint,{' '}
                <a className="govuk-link" href={EASS_URL} data-testid="public-accessibility-statement-link-eass">
                  contact the Equality Advisory and Support Service (EASS)
                </a>
                .
              </Paragraph>

              <H2>Technical information about this website’s accessibility</H2>
              <Paragraph>
                Share Progress Preventing Gambling Harms is committed to making its website accessible, in accordance with the Public Sector Bodies (Websites and Mobile
                Applications) (No. 2) Accessibility Regulations 2018.
              </Paragraph>

              <H3>Compliance Status</H3>
              <Paragraph>The website has been tested against the Web Content Accessibility Guidelines (WCAG) 2.2 AA standard.</Paragraph>
              <Paragraph>
                This website is partially compliant with the{' '}
                <a className="govuk-link" href={WCAG_URL} data-testid="public-accessibility-statement-link-wcag">
                  Web Content Accessibility Guidelines version 2.2
                </a>{' '}
                AA standard, due to the exemptions listed below.
              </Paragraph>

              <H2>Non-accessible content</H2>
              <Paragraph>The content listed below is non-accessible for the following reasons.</Paragraph>

              <H3>Non-compliance with the accessibility regulations</H3>
              <Paragraph>
                Text within dropdown menus across the ‘Organisations’, ‘Data Collections’, and ‘Reports’ pages does not adjust when text spacing or custom styling is applied. This
                may prevent users who require increased line or letter spacing from being able to read the content effectively. This fails WCAG 2.2 success criterion 1.4.12 (text
                spacing).
              </Paragraph>

              <H3>Disproportionate burden</H3>
              <Paragraph>We have not identified any accessibility problems that would be a disproportionate burden to fix.</Paragraph>

              <H3>Content that’s not within the scope of the accessibility regulations</H3>
              <Paragraph>There is no content on the website that is outside of the scope of accessibility regulations.</Paragraph>

              <H2>What we’re doing to improve accessibility</H2>
              <Paragraph>
                We are aware of these issues and are considering various solutions at this stage. At the moment, we do not have a set timeline for completion, but will provide
                updates as progress is made.
              </Paragraph>

              <H2>Preparation of this accessibility statement</H2>
              <Paragraph>This statement was prepared on 11/08/2026. It was last reviewed on 11/08/2026.</Paragraph>
              <Paragraph>This website was last tested on 11/08/2026. The test was carried out by Zoonou.</Paragraph>
              <Paragraph>Zoonou used WCAG-EM to define the pages tested and test approach.</Paragraph>
            </div>
          </div>
        </div>
      </main>

      <LayoutFooter />
    </div>
  );
};

export default AccessibilityStatement;
