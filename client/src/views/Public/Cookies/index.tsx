import React, { useLayoutEffect, useRef, useState } from 'react';
import usePageTitle from '../../../hooks/usePageTitle';
import { GovUKHeaderLogo } from '../../../components/Logos';
import {
  GovUKBreadcrumbs,
  GovUKButton,
  GovUKFieldset,
  GovUKNotificationBanner,
  GovUKNotificationDisplay,
  GovUKSkipLink,
  GovUKTable,
  H1,
  H2,
  Paragraph,
  useGovUKNotification,
} from '../../../components/GovUKComponents';
import type { Column } from '../../../components/GovUKComponents';
import { homeLinkForRole, useAuthProvider } from '../../../components/AuthProvider';
import HeaderBar from '../../../layouts/General/LayoutHeader/HeaderBar';
import LayoutFooter from '../../../layouts/General/LayoutFooter';
import { getCookiePreferences, saveCookiePreferences, type CookiePreferences } from '../../../helpers/cookiePolicy';
import './styles.css';

const SERVICE_NAME = 'Share progress preventing gambling harms';
const baseUrl = import.meta.env.BASE_URL || '/';

interface CookieRow {
  name: string;
  purpose: string;
  expiry: string;
}

// Confirmed strictly necessary cookies. mfa_session is set by the API after multi-factor sign-in;
// cookie_policy is set by this page. Both always need to be on, so they do not require consent. The
// analytics/functional inventory is a KEY DEPENDENCY (BLUM-cookies) pending data-protection sign-off
// before go-live — no such cookie is set unless the matching category is accepted here.
const essentialCookies: CookieRow[] = [
  {
    name: 'mfa_session',
    purpose:
      'Keeps you signed in after you complete multi-factor authentication, so you do not have to verify again on every page. Only set after you sign in. We record the IP address and a description of the browser and device against each session. This lets us show you on the Your account page where you are signed in, so you can end a session you do not recognise.',
    expiry: '8 hours, or 30 minutes of inactivity — whichever comes first',
  },
  {
    name: 'cookie_policy',
    purpose: 'Remembers the cookie choices you make on this page so we only use the optional cookies you have accepted.',
    expiry: '1 year',
  },
];

const cookieColumns: Column<CookieRow>[] = [
  { key: 'name', header: 'Name' },
  { key: 'purpose', header: 'Purpose' },
  { key: 'expiry', header: 'Expires' },
];

const analyticsOptions = [
  { value: 'accept', text: 'Use cookies that measure my website use' },
  { value: 'reject', text: 'Do not use cookies that measure my website use' },
];

const functionalOptions = [
  { value: 'accept', text: 'Use cookies that remember my settings on the service' },
  { value: 'reject', text: 'Do not use cookies that remember my settings on the service' },
];

const asChoice = (accepted: boolean): string => (accepted ? 'accept' : 'reject');

const Cookies = (): React.ReactElement => {
  // Counter, not a boolean: the banner stays mounted after the first save, so a plain flag would make a
  // second save a no-op and leave keyboard/screen-reader users with no feedback. Bumping the count on
  // every save re-runs the focus effect so each save re-announces the confirmation.
  const [saveCount, setSaveCount] = useState(0);
  const [preferences, setPreferences] = useState<CookiePreferences>(() => getCookiePreferences());
  const bannerRef = useRef<HTMLDivElement>(null);
  const { userRole } = useAuthProvider();
  const { setNotification } = useGovUKNotification();

  usePageTitle('Cookies');

  // Move focus to the success confirmation on each save so keyboard and screen-reader users are told their
  // choice was saved (the banner is also role="alert").
  useLayoutEffect(() => {
    if (saveCount > 0) {
      bannerRef.current?.focus();
    }
  }, [saveCount]);

  const handleSave = (event: React.FormEvent) => {
    event.preventDefault();
    saveCookiePreferences(preferences);
    // Retire any confirmation carried in from another page first. Since this page started rendering
    // GovUKNotificationDisplay, arriving with one (a draft saved on the way out of the activity wizard) and
    // then saving here would stack two role="alert" success banners, and focus would move to the lower one.
    setNotification(null);
    setSaveCount((count) => count + 1);
  };

  return (
    <div data-testid="public-cookies">
      <GovUKSkipLink />

      {/* The app's blue header bar, but without the sign-out link or the service-navigation bar — this is a
          public informational page, not an app surface. */}
      <header role="banner" data-testid="public-cookies-header">
        <HeaderBar>
          {/* Classes and the absence of an inline colour are load-bearing — see the crown link in
                layouts/General/LayoutHeader for the full reasoning (BLUM-6386). */}
          <a href={baseUrl} className="govuk-header__homepage-link govuk-link--inverse" data-testid="public-cookies-header-home-link">
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

              {saveCount > 0 && (
                <div ref={bannerRef} tabIndex={-1} className="public-cookies__banner">
                  <GovUKNotificationBanner type="success" title="Your cookie settings were saved" testId="public-cookies-saved-banner">
                    <Paragraph>We will only use the cookies you chose. You can change these settings on this page at any time.</Paragraph>
                  </GovUKNotificationBanner>
                </div>
              )}

              <H1>Cookies on {SERVICE_NAME}</H1>
              <Paragraph>Cookies are files saved on your phone, tablet or computer when you visit a website.</Paragraph>
              <Paragraph>We use cookies to make {SERVICE_NAME} work and to understand how you use it.</Paragraph>

              <form onSubmit={handleSave} noValidate>
                <H2>Cookie settings</H2>
                <Paragraph>We use 2 types of optional cookie. You can choose which cookies you are happy for us to use.</Paragraph>

                <GovUKFieldset.RadioGroup
                  name="analytics"
                  legend="Cookies that measure website use"
                  legendSize="m"
                  hint="We would like to use cookies to understand how people use the service and make it better. We are still finalising the exact cookies used — none are set unless you accept them here."
                  options={analyticsOptions}
                  value={asChoice(preferences.analytics)}
                  onChange={(value) => setPreferences((current) => ({ ...current, analytics: value === 'accept' }))}
                  testId="public-cookies-analytics"
                />

                <GovUKFieldset.RadioGroup
                  name="functional"
                  legend="Cookies that remember your settings"
                  legendSize="m"
                  hint="These cookies let the service remember choices you make to give you a better experience. We are still finalising the exact cookies used — none are set unless you accept them here."
                  options={functionalOptions}
                  value={asChoice(preferences.functional)}
                  onChange={(value) => setPreferences((current) => ({ ...current, functional: value === 'accept' }))}
                  testId="public-cookies-functional"
                />

                <div className="govuk-button-group">
                  <GovUKButton type="submit" data-testid="public-cookies-save">
                    Save changes
                  </GovUKButton>
                </div>
              </form>

              <H2>Strictly necessary cookies</H2>
              <Paragraph>
                These essential cookies do things like keep your sign-in secure and remember your cookie choices. They always need to be on, so we do not ask for your consent to
                use them.
              </Paragraph>
              <GovUKTable<CookieRow>
                label="Strictly necessary cookies"
                testId="public-cookies-essential-table"
                data={essentialCookies}
                columns={cookieColumns}
                keyExtractor={(cookie) => cookie.name}
                searchable={false}
              />
            </div>
          </div>
        </div>
      </main>

      <LayoutFooter />
    </div>
  );
};

export default Cookies;
