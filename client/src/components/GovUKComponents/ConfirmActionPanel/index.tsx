import React from 'react';
import './styles.css';

interface ConfirmActionPanelProps {
  children: React.ReactNode;
  testId?: string;
}

/**
 * Full-bleed deep-blue confirmation panel for destructive-action "Are you sure?"
 * pages (Deactivate, Discard edits, …). govuk-frontend ships govuk-panel--confirmation
 * but it's green-only — this is the bespoke blue variant used across the service.
 *
 * Render H1, Paragraph, and a govuk-button-group with a govuk-button--inverse
 * confirm + govuk-link--inverse cancel inside it. White typography and inverse
 * link/button colours come from govuk-frontend's modifier classes, so this
 * wrapper only owns the panel layout and the per-element colour overrides.
 */
const ConfirmActionPanel = ({ children, testId }: ConfirmActionPanelProps): React.ReactElement => (
  <div className="confirm-action-panel" data-testid={testId}>
    {children}
  </div>
);

export default ConfirmActionPanel;
