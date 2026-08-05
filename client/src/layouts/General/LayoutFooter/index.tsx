import React from 'react';
import {
  CopyrightLogo,
  CopyrightText,
  FooterContainer,
  FooterLicence,
  FooterLicenceText,
  FooterMeta,
  FooterMetaLink,
  FooterMetaRouterLink,
  FooterMetaLinks,
  FooterRoot,
  OglLogoWrapper,
} from './styles';
import CrestLogo from '../../../components/Logos/CrestLogo';
import CrownLogo from '../../../components/Logos/CrownLogo';
import { ROUTES } from '../../../helpers/routes';

// `internal` links stay inside the app (react-router, no new tab); the rest are gov.uk pages opened in a
// new tab. Cookies points at our own GDS cookies page (BLUM-cookies), not the generic gov.uk one.
const metaLinks: { label: string; href: string; internal?: boolean }[] = [
  { label: 'Help', href: 'https://www.gov.uk/help' },
  { label: 'Privacy', href: 'https://www.gov.uk/help/privacy-notice' },
  { label: 'Cookies', href: ROUTES.public.cookies, internal: true },
  { label: 'Accessibility statement', href: 'https://www.gov.uk/help/accessibility-statement' },
  { label: 'Contact', href: 'https://www.gov.uk/contact' },
  { label: 'Terms and conditions', href: 'https://www.gov.uk/help/terms-conditions' },
];

const metaLinksSecondRow: { label: string; href: string; lang?: string }[] = [
  { label: 'Rhestr o Wasanaethau Cymraeg', href: 'https://www.gov.uk/cymraeg', lang: 'cy' },
  { label: 'Government Digital Service', href: 'https://www.gov.uk/government/organisations/government-digital-service' },
];

const LayoutFooter = (): React.ReactElement => {
  return (
    <FooterRoot component="footer" role="contentinfo">
      <FooterContainer>
        <div className="app-footer__crown" aria-hidden="true">
          <CrownLogo color="#0b0c0c" width={32} height={30} />
        </div>

        <FooterMeta>
          <div className="app-footer__meta-content">
            <FooterMetaLinks>
              {metaLinks.map((link) =>
                link.internal ? (
                  <FooterMetaRouterLink key={link.label} to={link.href}>
                    {link.label}
                  </FooterMetaRouterLink>
                ) : (
                  <FooterMetaLink key={link.label} href={link.href} target="_blank" rel="noopener noreferrer">
                    {link.label}
                  </FooterMetaLink>
                )
              )}
            </FooterMetaLinks>
            <FooterMetaLinks sx={{ mt: 1 }}>
              {metaLinksSecondRow.map((link) => (
                <FooterMetaLink key={link.label} href={link.href} lang={link.lang} target="_blank" rel="noopener noreferrer">
                  {link.label}
                </FooterMetaLink>
              ))}
            </FooterMetaLinks>
            <FooterLicence>
              <OglLogoWrapper aria-hidden="true">
                <svg focusable="false" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 483.2 195.7" height="17" width="41">
                  <path
                    fill="currentColor"
                    d="M421.5 142.8V.1l-50.7 32.3v161.1h112.4v-50.7zm-122.3-9.6A47.12 47.12 0 0 1 221 97.8c0-26 21.1-47.1 47.1-47.1 16.7 0 31.4 8.7 39.7 21.8l42.7-27.2A97.63 97.63 0 0 0 268.1 0c-36.5 0-68.3 20.1-85.1 49.7A98 98 0 0 0 97.8 0C43.9 0 0 43.9 0 97.8s43.9 97.8 97.8 97.8c36.5 0 68.3-20.1 85.1-49.7a97.76 97.76 0 0 0 149.6 25.4l19.4 22.2h3v-87.8h-80l24.3 27.5zM97.8 145c-26 0-47.1-21.1-47.1-47.1s21.1-47.1 47.1-47.1 47.2 21 47.2 47S123.8 145 97.8 145"
                  />
                </svg>
              </OglLogoWrapper>
              <FooterLicenceText>
                All content is available under the{' '}
                <a href="https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/" target="_blank" rel="noopener noreferrer">
                  Open Government Licence v3.0
                </a>
                , except where otherwise stated
              </FooterLicenceText>
            </FooterLicence>
          </div>

          <CopyrightLogo>
            <CrestLogo width={90} height={80} />
            <CopyrightText
              href="https://www.nationalarchives.gov.uk/information-management/re-using-public-sector-information/uk-government-licensing-framework/crown-copyright/"
              target="_blank"
              rel="noopener noreferrer">
              © Crown copyright
            </CopyrightText>
          </CopyrightLogo>
        </FooterMeta>
      </FooterContainer>
    </FooterRoot>
  );
};

export default LayoutFooter;
