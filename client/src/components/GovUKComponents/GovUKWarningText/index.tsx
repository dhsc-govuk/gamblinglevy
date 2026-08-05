import React from 'react';

interface GovUKWarningTextProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode;
  iconFallbackText?: string;
}

function GovUKWarningText({
  children,
  iconFallbackText = 'Warning',
  className = '',
  ...rest
}: GovUKWarningTextProps): React.ReactElement {
  return (
    <div className={`govuk-warning-text ${className}`.trim()} {...rest}>
      <span className="govuk-warning-text__icon" aria-hidden="true">!</span>
      <strong className="govuk-warning-text__text">
        <span className="govuk-visually-hidden">{iconFallbackText}</span>
        {children}
      </strong>
    </div>
  );
}

export default GovUKWarningText;
