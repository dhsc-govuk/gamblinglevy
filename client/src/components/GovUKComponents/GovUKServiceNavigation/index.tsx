import React from 'react';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';

interface NavigationItem {
  href: string;
  text: string;
  active?: boolean;
}

interface GovUKServiceNavigationProps {
  serviceName: string;
  serviceUrl?: string;
  navigation?: NavigationItem[];
  className?: string;
  testId?: string;
}

function GovUKServiceNavigation({
  serviceName,
  serviceUrl = '/',
  navigation,
  className = '',
  testId = 'service-nav',
}: GovUKServiceNavigationProps): React.ReactElement {
  return (
    <div
      className={`govuk-service-navigation ${className}`.trim()}
      data-module="govuk-service-navigation"
      data-testid={testId}
    >
      <div className="govuk-width-container">
        <div className="govuk-service-navigation__container">
          <span className="govuk-service-navigation__service-name">
            <a
              href={serviceUrl}
              className="govuk-service-navigation__link"
              data-testid={composeTestId(testId, 'service-link')}
            >
              {serviceName}
            </a>
          </span>
          {navigation && navigation.length > 0 && (
            <nav aria-label="Menu" className="govuk-service-navigation__wrapper">
              <ul className="govuk-service-navigation__list">
                {navigation.map((item, index) => (
                  <li
                    key={index}
                    className={`govuk-service-navigation__item${item.active ? ' govuk-service-navigation__item--active' : ''}`}
                  >
                    <a
                      className="govuk-service-navigation__link"
                      href={item.href}
                      data-testid={composeTestId(testId, 'item', slugifyTestId(item.text) || String(index))}
                      {...(item.active ? { 'aria-current': 'page' } : {})}
                    >
                      {item.text}
                    </a>
                  </li>
                ))}
              </ul>
            </nav>
          )}
        </div>
      </div>
    </div>
  );
}

export default GovUKServiceNavigation;
