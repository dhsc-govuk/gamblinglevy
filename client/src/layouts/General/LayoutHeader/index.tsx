import React, { useState, useEffect } from 'react';
import { Box } from '@mui/material';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { GovUKHeaderLogo } from '../../../components/Logos';
import { useAuthProvider, navigationForRole, homeLinkForRole, type HeaderNavItem } from '../../../components/AuthProvider';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import { ROUTES } from '../../../helpers/routes';
import { Header, HeaderContainer, SignOutLink, govukWhite } from './styles';
import './service-navigation.css';

const baseUrl = import.meta.env.BASE_URL || '/';

interface Props {
  serviceName?: string;
  navigation?: HeaderNavItem[];
}

const LayoutHeader = ({ serviceName = 'Share progress preventing gambling harms', navigation }: Props): React.ReactElement => {
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
    <Box component="header" role="banner">
      {/* GOV.UK Header — blue bar with logo and sign-out link */}
      <Header>
        <HeaderContainer sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <a href={baseUrl} className="govuk-header__link govuk-header__link--homepage" style={{ color: govukWhite, textDecoration: 'none' }}>
            <GovUKHeaderLogo />
          </a>

          {/* Account link then Sign out, in the GOV.UK account-navigation order. This is the only route into
              "where you're signed in" (BLUM-7241), and it has to be reachable from every page rather than
              buried in a role's service navigation, because the whole point is noticing a session you did
              not start. */}
          {/* govuk-link--inverse, not govuk-header__link: the latter does not exist in govuk-frontend 6, and
              naming it opts the link out of the app-wide a:not(.govuk-header__link):focus rule — leaving the
              only control in the blue bar without the yellow #ffdd00 focus outline. gap is govuk-spacing(3);
              no colour or type token is overridden here. */}
          <nav aria-label="Account" style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
            <RouterLink to={ROUTES.account.sessions} className="govuk-link govuk-link--inverse govuk-!-font-size-19" data-testid="header-account">
              Your account
            </RouterLink>

            <SignOutLink type="button" onClick={signOut} data-testid="header-sign-out">
              Sign out
            </SignOutLink>
          </nav>
        </HeaderContainer>
      </Header>

      {/* Service Navigation — GDS-compliant light-blue bar */}
      <section aria-label="Service information" className="govuk-service-navigation" data-module="govuk-service-navigation">
        <div className="govuk-width-container">
          <div className="govuk-service-navigation__container">
            <span className="govuk-service-navigation__service-name">
              <RouterLink to={homeUrl} className="govuk-service-navigation__link">
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
                  hidden={!mounted}>
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
    </Box>
  );
};

export default LayoutHeader;
