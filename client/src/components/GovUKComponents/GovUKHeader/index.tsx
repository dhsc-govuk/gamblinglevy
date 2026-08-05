import React from 'react';
import { GovUKHeaderLogo } from '../../Logos';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';

interface GovUKHeaderProps {
  serviceName?: string;
  serviceUrl?: string;
  navigation?: Array<{
    href: string;
    text: string;
    active?: boolean;
  }>;
  children?: React.ReactNode;
  testId?: string;
}

function GovUKHeader({
  serviceName,
  serviceUrl = '/',
  navigation,
  children,
  testId = 'app-header',
}: GovUKHeaderProps): React.ReactElement {
  return (
    <header className="govuk-header" data-module="govuk-header" data-testid={testId}>
      <div className="govuk-header__container govuk-width-container">
        <div className="govuk-header__logo">
          <a
            href="https://www.gov.uk"
            className="govuk-header__link govuk-header__link--homepage"
            data-testid={composeTestId(testId, 'home-link')}
          >
              <GovUKHeaderLogo />
            </a>
        </div>
        {serviceName && (
          <div className="govuk-header__content">
            <a
              href={serviceUrl}
              className="govuk-header__link govuk-header__service-name"
              data-testid={composeTestId(testId, 'service-name')}
            >
              {serviceName}
            </a>
            {navigation && navigation.length > 0 && (
              <nav aria-label="Menu" className="govuk-header__navigation" data-testid={composeTestId(testId, 'nav')}>
                <ul className="govuk-header__navigation-list">
                  {navigation.map((item, index) => (
                    <li
                      key={index}
                      className={`govuk-header__navigation-item${item.active ? ' govuk-header__navigation-item--active' : ''}`}
                    >
                      <a
                        className="govuk-header__link"
                        href={item.href}
                        data-testid={composeTestId(testId, 'nav-item', slugifyTestId(item.text) || String(index))}
                      >
                        {item.text}
                      </a>
                    </li>
                  ))}
                </ul>
              </nav>
            )}
          </div>
        )}
        {children}
      </div>
    </header>
  );
}

export default GovUKHeader;
