import React from 'react';
import './styles.css';

export type TagColour = 
  | 'grey' 
  | 'green' 
  | 'turquoise' 
  | 'blue' 
  | 'light-blue' 
  | 'purple' 
  | 'pink' 
  | 'red' 
  | 'orange' 
  | 'yellow'
  | 'white';

interface GovUKTagProps extends React.HTMLAttributes<HTMLElement> {
  children: React.ReactNode;
  colour?: TagColour;
}

function GovUKTag({ children, colour, className = '', ...rest }: GovUKTagProps): React.ReactElement {
  const colourClass = colour ? `govuk-tag--${colour}` : '';

  return (
    <strong className={`govuk-tag ${colourClass} ${className}`.trim()} {...rest}>
      {children}
    </strong>
  );
}

export default GovUKTag;
