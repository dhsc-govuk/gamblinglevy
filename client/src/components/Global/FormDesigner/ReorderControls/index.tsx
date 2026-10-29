import React from 'react';

interface Props {
  onMoveUp: () => void;
  onMoveDown: () => void;
  isFirst: boolean;
  isLast: boolean;
  label: string;
  testId?: string;
}

const disabledStyle: React.CSSProperties = {
  color: '#b1b4b6',
  cursor: 'not-allowed',
  textDecoration: 'none',
};

// Keyboard-accessible reorder controls (WCAG 2.2 AA — drag-only would fail). Used for sections, questions and
// options in the Form Designer.
const ReorderControls = ({ onMoveUp, onMoveDown, isFirst, isLast, label, testId }: Props): React.ReactElement => (
  <span className="govuk-!-margin-right-2">
    <button
      type="button"
      className="govuk-link"
      style={isFirst ? disabledStyle : undefined}
      disabled={isFirst}
      onClick={onMoveUp}
      data-testid={testId ? `${testId}-move-up` : undefined}>
      Up<span className="govuk-visually-hidden"> — move {label} up</span>
    </button>
    <span aria-hidden="true"> · </span>
    <button
      type="button"
      className="govuk-link"
      style={isLast ? disabledStyle : undefined}
      disabled={isLast}
      onClick={onMoveDown}
      data-testid={testId ? `${testId}-move-down` : undefined}>
      Down<span className="govuk-visually-hidden"> — move {label} down</span>
    </button>
  </span>
);

export default ReorderControls;
