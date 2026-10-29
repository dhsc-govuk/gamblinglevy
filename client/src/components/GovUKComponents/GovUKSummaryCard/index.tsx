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
  // The title's heading level. Defaults to h2 (the Design System example); a card nested under an h3 (the DHSC
  // maturity-assessment detail lists sections under one) passes 4 so the outline stays a tree.
  headingLevel?: 2 | 3 | 4;
  testId?: string;
}

// Native <button> ships its own tiny UA-default font — buttons don't inherit font-family/size/weight from
// ancestors the way normal elements do, so without `font: inherit` this renders far smaller than the
// 19px/700 that `.govuk-summary-card__actions` already sets natively on the surrounding <li>/<ul>.
// The chrome reset (background, border, padding) is `button.govuk-link` in govuk-refreshed-branding.css,
// never here: an inline background also hides the yellow focus fill.
const actionButtonStyle: React.CSSProperties = {
  font: 'inherit',
};

/**
 * GOV.UK Summary card component.
 * Uses native govuk-frontend classes (.govuk-summary-card) so styling comes
 * directly from the Design System. See https://design-system.service.gov.uk/components/summary-list/#summary-cards
 */
function GovUKSummaryCard({ title, actions, children, headingLevel = 2, testId }: GovUKSummaryCardProps): React.ReactElement {
  const TitleTag = `h${headingLevel}` as 'h2' | 'h3' | 'h4';

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
          <TitleTag className="govuk-summary-card__title" data-testid={composeTestId(testId, 'title')}>
            {title}
          </TitleTag>
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
