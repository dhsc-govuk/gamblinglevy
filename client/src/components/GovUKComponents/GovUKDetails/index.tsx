import React from 'react';

interface GovUKDetailsProps extends Omit<React.DetailsHTMLAttributes<HTMLDetailsElement>, 'children'> {
  summary: string;
  children: React.ReactNode;
}

function GovUKDetails({
  summary,
  children,
  open = false,
  className = '',
  ...rest
}: GovUKDetailsProps): React.ReactElement {
  return (
    <details className={`govuk-details ${className}`.trim()} open={open} {...rest}>
      <summary className="govuk-details__summary">
        <span className="govuk-details__summary-text">
          {summary}
        </span>
      </summary>
      <div className="govuk-details__text">
        {children}
      </div>
    </details>
  );
}

export default GovUKDetails;
