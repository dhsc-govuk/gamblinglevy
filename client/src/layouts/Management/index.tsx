import React from 'react';
import GeneralLayout from '../General';
import { GovUKBackLink, GovUKBreadcrumbs, GovUKSideNavigation } from '../../components/GovUKComponents';
import { ROUTES } from '../../helpers/routes';
import useFeature from '../../hooks/useFeature';
import './styles.css';

interface Props extends React.PropsWithChildren {
  breadcrumbs?: { label: string; link: string }[];
  backLink?: { href: string; onClick?: () => void; testId?: string };
}

const managementNavigation = [
  { text: 'Organisations', href: ROUTES.admin.organisations.list },
  { text: 'Users', href: ROUTES.admin.users.list },
  { text: 'Data collections', href: ROUTES.admin.collections.list },
  { text: 'Audit log', href: ROUTES.admin.auditLogs.list },
];

// The forms builder is routed only while BLUM_9087 is on, so its link is listed only then.
const formsNavigation = [...managementNavigation, { text: 'Forms', href: ROUTES.admin.forms.list }];

const ManagementLayout = ({ children, breadcrumbs = [], backLink }: Props): React.ReactElement => {
  const formsEnabled = useFeature('BLUM_9087');
  return (
    <GeneralLayout mainContentTarget="nested">
      <div className="govuk-grid-row management-layout">
        <div className="govuk-grid-column-one-quarter management-layout__navigation">
          <GovUKSideNavigation items={formsEnabled ? formsNavigation : managementNavigation} label="Management" testId="management-side-navigation" />
        </div>
        <div className="govuk-grid-column-three-quarters management-layout__content">
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
};

export default ManagementLayout;
