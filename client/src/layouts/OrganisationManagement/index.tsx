import React from 'react';
import GeneralLayout from '../General';
import { GovUKBackLink, GovUKBreadcrumbs, GovUKSideNavigation } from '../../components/GovUKComponents';
import './styles.css';

interface Props extends React.PropsWithChildren {
  breadcrumbs?: { label: string; link: string }[];
  backLink?: { href: string; onClick?: () => void; testId?: string };
}

const organisationNavigation = [
  {
    text: 'Organisation',
    href: '/organisation-admin/home',
    nested: [{ text: 'Users', href: '/organisation-admin/users' }],
  },
];

const OrganisationManagementLayout = ({ children, breadcrumbs = [], backLink }: Props): React.ReactElement => (
  <GeneralLayout mainContentTarget="nested">
    <div className="govuk-grid-row organisation-management-layout">
      <div className="govuk-grid-column-one-quarter organisation-management-layout__navigation">
        <GovUKSideNavigation items={organisationNavigation} label="Organisation navigation" testId="organisation-side-navigation" />
      </div>
      <div className="govuk-grid-column-three-quarters organisation-management-layout__content">
        {breadcrumbs.length > 0 && <GovUKBreadcrumbs items={breadcrumbs.map((item) => ({ label: item.label, href: item.link }))} />}
        {backLink && (
          <GovUKBackLink
            href={backLink.href}
            data-testid={backLink.testId}
            onClick={
              backLink.onClick
                ? (e) => {
                    e.preventDefault();
                    backLink.onClick?.();
                  }
                : undefined
            }
          />
        )}
        <div id="main-content" tabIndex={-1}>
          {children}
        </div>
      </div>
    </div>
  </GeneralLayout>
);

export default OrganisationManagementLayout;
