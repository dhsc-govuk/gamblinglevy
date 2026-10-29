import React from 'react';
import { GovUKBreadcrumbs, GovUKBackLink, GovUKNotificationDisplay } from '../../../components/GovUKComponents';
import './styles.css';

interface Props extends React.PropsWithChildren {
  breadcrumbs?: { label: string; link: string }[];
  endContent?: React.ReactElement;
  backLink?: { href?: string; onClick?: () => void; testId?: string; className?: string };
  isMainContentTarget?: boolean;
}

const Content = ({ children, breadcrumbs, endContent, backLink, isMainContentTarget = true }: Props): React.ReactElement => {
  const hasBreadcrumbsAndBackLink = Boolean(breadcrumbs?.length && backLink);

  return (
    <main className="govuk-main-wrapper" role="main">
      <div className={`govuk-width-container${hasBreadcrumbsAndBackLink ? ' govuk-width-container--breadcrumb-back-link' : ''}`}>
        {breadcrumbs && breadcrumbs.length > 0 && <GovUKBreadcrumbs items={breadcrumbs.map((item) => ({ label: item.label, href: item.link }))} />}
        {backLink && (
          <GovUKBackLink
            href={backLink.href}
            className={backLink.className}
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
        {endContent && (
          <div className="content-header">
            <div className="content-header__actions">{endContent}</div>
          </div>
        )}

        <GovUKNotificationDisplay />
        <div className="content-body" id={isMainContentTarget ? 'main-content' : undefined} tabIndex={isMainContentTarget ? -1 : undefined}>
          {children}
        </div>
      </div>
    </main>
  );
};

export default Content;
