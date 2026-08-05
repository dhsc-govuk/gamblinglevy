import React from 'react';
import { Link } from 'react-router-dom';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';

interface BreadcrumbItem {
  label: string;
  href?: string;
}

interface GovUKBreadcrumbsProps {
  items: BreadcrumbItem[];
  testId?: string;
}

function GovUKBreadcrumbs({ items, testId = 'breadcrumbs' }: GovUKBreadcrumbsProps): React.ReactElement {
  return (
    <nav className="govuk-breadcrumbs" aria-label="Breadcrumb" data-testid={testId}>
      <ol className="govuk-breadcrumbs__list">
        {items.map((item, index) => {
          const itemTestId = composeTestId(testId, 'item', slugifyTestId(item.label) || String(index));
          return (
            <li className="govuk-breadcrumbs__list-item" key={index}>
              {item.href ? (
                <Link className="govuk-breadcrumbs__link" to={item.href} data-testid={itemTestId}>
                  {item.label}
                </Link>
              ) : (
                <span aria-current="page" data-testid={itemTestId}>
                  {item.label}
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

export default GovUKBreadcrumbs;
