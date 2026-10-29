import React from 'react';

interface GovUKPanelProps extends React.HTMLAttributes<HTMLDivElement> {
  title: string;
  children?: React.ReactNode;
}

function GovUKPanel({ title, children, className = '', ...rest }: GovUKPanelProps): React.ReactElement {
  return (
    <div className={`govuk-panel govuk-panel--confirmation ${className}`.trim()} {...rest}>
      <h1 className="govuk-panel__title">{title}</h1>
      {children && <div className="govuk-panel__body">{children}</div>}
    </div>
  );
}

export default GovUKPanel;
