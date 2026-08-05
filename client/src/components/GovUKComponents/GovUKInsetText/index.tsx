import React from 'react';

interface GovUKInsetTextProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode;
}

function GovUKInsetText({ children, className = '', ...rest }: GovUKInsetTextProps): React.ReactElement {
  return (
    <div className={`govuk-inset-text ${className}`.trim()} {...rest}>
      {children}
    </div>
  );
}

export default GovUKInsetText;
