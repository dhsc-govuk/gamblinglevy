import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { composeTestId } from '../../../helpers/testIds';

export interface PaginationLink {
  // The destination's title, shown under "Previous" / "Next".
  label: string;
  href: string;
}

interface GovUKPaginationProps {
  previous?: PaginationLink;
  next?: PaginationLink;
  // Names the landmark for assistive tech; two paginations on one page need distinct names.
  ariaLabel?: string;
  testId?: string;
}

// The GOV.UK Design System's block-style pagination (govuk-pagination--block): a Previous / Next pair with labels,
// used to step between the sections of a submitted maturity assessment. Native govuk-frontend markup; the links are
// react-router links so navigation stays in the SPA.
function GovUKPagination({ previous, next, ariaLabel = 'Pagination', testId }: GovUKPaginationProps): React.ReactElement | null {
  if (!previous && !next) {
    return null;
  }

  return (
    <nav className="govuk-pagination govuk-pagination--block" aria-label={ariaLabel} data-testid={testId}>
      {previous && (
        <div className="govuk-pagination__prev">
          <RouterLink className="govuk-link govuk-pagination__link" to={previous.href} rel="prev" data-testid={composeTestId(testId, 'prev')}>
            <svg
              className="govuk-pagination__icon govuk-pagination__icon--prev"
              xmlns="http://www.w3.org/2000/svg"
              height="13"
              width="15"
              aria-hidden="true"
              focusable="false"
              viewBox="0 0 15 13">
              <path d="m6.5938-0.0078125-6.7266 6.7266 6.7441 6.4062 1.377-1.449-4.1856-3.9768h12.896v-2h-12.984l4.2931-4.293-1.414-1.414z"></path>
            </svg>
            <span className="govuk-pagination__link-title">
              Previous<span className="govuk-visually-hidden"> section</span>
            </span>
            <span className="govuk-visually-hidden">:</span>
            <span className="govuk-pagination__link-label">{previous.label}</span>
          </RouterLink>
        </div>
      )}
      {next && (
        <div className="govuk-pagination__next">
          <RouterLink className="govuk-link govuk-pagination__link" to={next.href} rel="next" data-testid={composeTestId(testId, 'next')}>
            <span className="govuk-pagination__link-title">
              Next<span className="govuk-visually-hidden"> section</span>
            </span>
            <span className="govuk-visually-hidden">:</span>
            <span className="govuk-pagination__link-label">{next.label}</span>
            <svg
              className="govuk-pagination__icon govuk-pagination__icon--next"
              xmlns="http://www.w3.org/2000/svg"
              height="13"
              width="15"
              aria-hidden="true"
              focusable="false"
              viewBox="0 0 15 13">
              <path d="m8.107-0.0078125-1.4136 1.414 4.2926 4.293h-12.986v2h12.896l-4.1855 3.9766 1.377 1.4492 6.7441-6.4062-6.7246-6.7266z"></path>
            </svg>
          </RouterLink>
        </div>
      )}
    </nav>
  );
}

export default GovUKPagination;
