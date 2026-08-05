import React from 'react';
import { Link } from 'react-router-dom';
import './styles.css';

interface GovUKBackLinkProps {
  href?: string;
  children?: React.ReactNode;
  onClick?: (e: React.MouseEvent) => void;
  className?: string;
  'data-testid'?: string;
}

/**
 * GOV.UK back link component.
 * - When a real href is provided: renders as a React Router Link (SPA navigation)
 * - When no href is provided: renders as a button styled as a link (JS-only navigation, e.g. multi-step forms)
 */
function GovUKBackLink({ href, children = 'Back', onClick, className, ...rest }: GovUKBackLinkProps): React.ReactElement {
  const testId = rest['data-testid'];
  const classes = ['govuk-back-link', className].filter(Boolean).join(' ');
  if (href && href !== '#') {
    return (
      <Link to={href} className={classes} onClick={onClick} data-testid={testId}>
        {children}
      </Link>
    );
  }

  return (
    <button
      type="button"
      className={classes}
      onClick={(e) => {
        onClick?.(e);
      }}
      data-testid={testId}>
      {children}
    </button>
  );
}

export default GovUKBackLink;
