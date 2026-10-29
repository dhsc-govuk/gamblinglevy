import React, { useState, useEffect } from 'react';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { GovUKHeaderLogo } from '../../../components/Logos';
import { useAuthProvider, navigationForRole, homeLinkForRole, type HeaderNavItem } from '../../../components/AuthProvider';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import { ROUTES } from '../../../helpers/routes';
import { FEEDBACK_URL } from '../../../helpers/constants';
import { SERVICE_NAME } from '../../../hooks/usePageTitle';
import { GovUKPhaseBanner } from '../../../components/GovUKComponents';
import HeaderBar from './HeaderBar';
import './service-navigation.css';

const baseUrl = import.meta.env.BASE_URL || '/';

interface Props {
  serviceName?: string;
  navigation?: HeaderNavItem[];
}

const LayoutHeader = ({ serviceName = SERVICE_NAME, navigation }: Props): React.ReactElement => {
  const location = useLocation();
  const { userRole, signOut } = useAuthProvider();

  const effectiveNavigation = navigation ?? navigationForRole(userRole);

  // Mobile nav toggle — mirrors the GDS JS behaviour:
  // [hidden] is removed from the toggle button after mount (JS available),
  // then aria-expanded drives the CSS sibling selector that shows/hides the list.
  const [mounted, setMounted] = useState(false);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  const isActive = (item: HeaderNavItem) => {
    const prefixes = item.activePrefixes ?? [item.href];
    return prefixes.some((prefix) => location.pathname.startsWith(prefix));
  };

  const homeUrl = homeLinkForRole(userRole);

  return (
    <header role="banner" data-testid="header">
      {/* GOV.UK Header — blue bar with logo and sign-out link */}
      <HeaderBar>
        {/* govuk-header__homepage-link (v6's name; govuk-header__link--homepage never existed in v6) plus
              govuk-link--inverse, which is what keeps the app-wide a:not(…) rule in
              styles/govuk-refreshed-branding.css off it — without it that rule wins on specificity and paints
              the crown #1d70b8 on the #1d70b8 bar. No inline colour: the white comes from the vendor
              :link rule, and an inline colour would override the :focus rule's #0b0c0c and leave the
              logotype white on the yellow focus block at 1.35:1 (BLUM-6386). Note the vendor's
              :not(:focus){background-color:#1d70b8} assumes this bar is exactly #1d70b8 (.app-header-bar in
              ./styles.css) — change the bar colour and a blue rectangle appears behind the crown. */}
        <a href={baseUrl} className="govuk-header__homepage-link govuk-link--inverse" data-testid="header-home-link">
          <GovUKHeaderLogo />
        </a>

        {/* Account link then Sign out, in the GOV.UK account-navigation order. This is the only route into
              "where you're signed in" (BLUM-7241), and it has to be reachable from every page rather than
              buried in a role's service navigation, because the whole point is noticing a session you did
              not start. */}
        {/* govuk-link--inverse, not govuk-header__link: the latter does not exist in govuk-frontend 6, and
              naming it left the link matching no :focus rule in either stylesheet — leaving the only control
              in the blue bar without the yellow #ffdd00 focus outline. gap is govuk-spacing(3); no colour or
              type token is overridden here. */}
        <nav aria-label="Account" style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
          <RouterLink to={ROUTES.account.sessions} className="govuk-link govuk-link--inverse govuk-!-font-size-19" data-testid="header-account">
            Your account
          </RouterLink>

          <button type="button" className="app-header-bar__sign-out" onClick={signOut} data-testid="header-sign-out">
            Sign out
          </button>
        </nav>
      </HeaderBar>

      {/* Service Navigation — GDS-compliant light-blue bar */}
      <section aria-label="Service information" className="govuk-service-navigation" data-module="govuk-service-navigation">
        <div className="govuk-width-container">
          <div className="govuk-service-navigation__container">
            <span className="govuk-service-navigation__service-name">
              <RouterLink to={homeUrl} className="govuk-service-navigation__link" data-testid="header-service-link">
                {serviceName}
              </RouterLink>
            </span>

            {effectiveNavigation.length > 0 && (
              <nav aria-label="Menu" className="govuk-service-navigation__wrapper">
                <button
                  type="button"
                  className="govuk-service-navigation__toggle govuk-js-service-navigation-toggle"
                  aria-controls="navigation"
                  aria-expanded={mobileNavOpen ? 'true' : 'false'}
                  onClick={() => setMobileNavOpen((open) => !open)}
                  hidden={!mounted}
                  data-testid="header-menu-toggle">
                  Menu
                </button>

                <ul className="govuk-service-navigation__list" id="navigation">
                  {effectiveNavigation.map((item) => {
                    const active = isActive(item);
                    return (
                      <li key={item.label} className={`govuk-service-navigation__item${active ? ' govuk-service-navigation__item--active' : ''}`}>
                        <RouterLink
                          to={item.href}
                          className="govuk-service-navigation__link"
                          aria-current={active ? 'page' : undefined}
                          data-testid={composeTestId('header-nav', slugifyTestId(item.label))}>
                          {active ? <strong className="govuk-service-navigation__active-fallback">{item.label}</strong> : item.label}
                        </RouterLink>
                      </li>
                    );
                  })}
                </ul>
              </nav>
            )}
          </div>
        </div>
      </section>

      {/* Beta phase banner — on every authenticated page, i.e. every page rendered through GeneralLayout
          (BLUM-7060 AC1 / BLUM-7102 / BLUM-7661). views/Public/* and views/Mfa/* render other chrome and
          therefore show no banner; that matches pre-revert behaviour and is a decision, not an oversight.
          THIS IS THE ONLY PLACE IT IS RENDERED. ManagementLayout and OrganisationManagementLayout both
          render GeneralLayout inside themselves, so this one mount is what puts the banner — and the only
          route to the feedback form — on every authenticated page. There is deliberately no opt-out prop:
          the original BLUM-7060 implementation had one, the management layouts passed it false, and the
          banner went missing from almost every page (BLUM-7102). Deleting this block breaks nothing that
          any test, lint rule or build step can see; it has already been lost once, as collateral in an
          unrelated commit (7207212), which is what BLUM-7661 was raised against.

          It lives inside the `banner` landmark rather than in the GOV.UK template's usual `beforeContent`
          slot, which is landmark-less: that placement orphans the banner from landmark navigation (a
          screen-reader user going header -> main -> contentinfo never reaches the feedback link) and trips
          axe's `region` rule on every page, which this repo surfaces in dev via @axe-core/react. Measured:
          the template placement reports 1 `region` violation, here it reports 0, and the rendering is
          identical either way. It must stay OUTSIDE <main> so skip-to-main-content lands on the page
          content — which also rules out Content/index.tsx, since that renders the <main> element. */}
      <div className="govuk-width-container">
        <GovUKPhaseBanner phase="beta" feedbackUrl={FEEDBACK_URL} />
      </div>
    </header>
  );
};

export default LayoutHeader;
