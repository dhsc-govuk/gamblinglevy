import React from 'react';
import { composeTestId } from '../../../helpers/testIds';

interface GovUKDetailsProps extends Omit<React.DetailsHTMLAttributes<HTMLDetailsElement>, 'children'> {
  summary: string;
  children: React.ReactNode;
  'data-testid'?: string;
}

// `data-testid` lands on the <details> root like every other attribute; the <summary> — the element a user
// actually clicks to open it — derives `{testid}-summary` from it, and gets nothing when no testid is passed.
function GovUKDetails({ summary, children, open = false, className = '', 'data-testid': dataTestId, ...rest }: GovUKDetailsProps): React.ReactElement {
  return (
    <details className={`govuk-details ${className}`.trim()} open={open} {...rest} data-testid={dataTestId}>
      <summary className="govuk-details__summary" data-testid={composeTestId(dataTestId, 'summary')}>
        <span className="govuk-details__summary-text">{summary}</span>
      </summary>
      <div className="govuk-details__text">{children}</div>
    </details>
  );
}

export default GovUKDetails;
