import React from 'react';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';

export interface SummaryCardAction {
  label: string;
  onClick: () => void;
  visuallyHiddenText?: string;
  testId?: string;
}

interface GovUKSummaryCardProps {
  title?: React.ReactNode;
  actions?: SummaryCardAction[];
  children: React.ReactNode;
  testId?: string;
}

// Native <button> ships browser chrome (border + grey background) *and* its own tiny UA-default font —
// buttons don't inherit font-family/size/weight from ancestors the way normal elements do, so without
// `font: inherit` this renders far smaller than the 19px/700 that `.govuk-summary-card__actions`
// already sets natively on the surrounding <li>/<ul>. Reset the chrome and force the font to inherit so
// it matches the Design System's actual "Edit"/"Change" link size.
const actionButtonStyle: React.CSSProperties = {
  background: 'none',
  border: 0,
  padding: 0,
  cursor: 'pointer',
  font: 'inherit',
};

/**
 * GOV.UK Summary card component.
 * Uses native govuk-frontend classes (.govuk-summary-card) so styling comes
 * directly from the Design System. See https://design-system.service.gov.uk/components/summary-list/#summary-cards
 */
function GovUKSummaryCard({ title, actions, children, testId }: GovUKSummaryCardProps): React.ReactElement {
  const renderActionButton = (action: SummaryCardAction, index: number) => (
    <button
      type="button"
      className="govuk-link"
      style={actionButtonStyle}
      onClick={action.onClick}
      data-testid={action.testId ?? composeTestId(testId, 'action', slugifyTestId(action.label) || String(index))}>
      {action.label}
      {action.visuallyHiddenText && <span className="govuk-visually-hidden"> {action.visuallyHiddenText}</span>}
    </button>
  );

  return (
    <div className="govuk-summary-card" data-testid={testId}>
      <div className="govuk-summary-card__title-wrapper">
        {title !== undefined && (
          <h2 className="govuk-summary-card__title" data-testid={composeTestId(testId, 'title')}>
            {title}
          </h2>
        )}
        {actions &&
          actions.length > 0 &&
          (actions.length === 1 ? (
            <div className="govuk-summary-card__actions">{renderActionButton(actions[0], 0)}</div>
          ) : (
            <ul className="govuk-summary-card__actions">
              {actions.map((action, index) => (
                <li className="govuk-summary-card__action" key={index}>
                  {renderActionButton(action, index)}
                </li>
              ))}
            </ul>
          ))}
      </div>
      <div className="govuk-summary-card__content">{children}</div>
    </div>
  );
}

export default GovUKSummaryCard;
