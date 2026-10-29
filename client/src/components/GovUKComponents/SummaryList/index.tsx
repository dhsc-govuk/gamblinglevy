import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';

interface SummaryItem {
  label: string;
  // Omit for key + action-only rows (e.g. the deleted-activities list) — no value cell is rendered.
  value?: string | React.ReactNode;
  onAction?: () => void;
  // Renders the action as an internal RouterLink (SPA navigation, optional location state). Ignored when onAction
  // is set.
  actionTo?: string;
  actionState?: unknown;
  actionLabel?: string;
  isSubAnswer?: boolean;
  testId?: string;
}

interface SummaryListProps {
  items: SummaryItem[];
  noBorder?: boolean;
  noOuterBorder?: boolean;
  equalColumns?: boolean;
  halfWidthColumns?: boolean;
  // Drops the decorative "— " before a sub-answer's key, keeping the border grouping and lighter key weight. The
  // maturity assessment's matrix rows are drawn without it (Figma 2744-183718), and they are today the only rows
  // anywhere that set isSubAnswer — so nothing currently renders the dash. The branch stays because the dash is
  // the shared default a future sub-answer list would expect; this prop is the opt-out, not the other way round.
  plainSubAnswers?: boolean;
  testId?: string;
}

function SummaryList({
  items,
  noBorder = false,
  noOuterBorder = false,
  equalColumns = false,
  halfWidthColumns = false,
  plainSubAnswers = false,
  testId,
}: SummaryListProps): React.ReactElement {
  const listClassName = ['govuk-summary-list', noBorder ? 'govuk-summary-list--no-border' : ''].filter(Boolean).join(' ');

  const getRowStyle = (index: number, item: SummaryItem): React.CSSProperties => {
    const isLastItem = index === items.length - 1;
    const nextItem = items[index + 1];
    const isFollowedBySubAnswer = nextItem?.isSubAnswer === true;
    const prevItem = items[index - 1];
    const isFirstSubAnswer = item.isSubAnswer && !prevItem?.isSubAnswer;

    if (!noOuterBorder) {
      // When not using noOuterBorder, still group sub-answers with parent
      if (isFollowedBySubAnswer) {
        return { borderBottom: 'none', paddingBottom: 0 };
      }
      if (item.isSubAnswer) {
        return {
          borderBottom: isLastItem || !nextItem?.isSubAnswer ? undefined : 'none',
          paddingTop: isFirstSubAnswer ? '10px' : '5px',
          paddingBottom: isLastItem || !nextItem?.isSubAnswer ? undefined : '5px',
        };
      }
      return {};
    }

    // noOuterBorder mode
    const baseStyle: React.CSSProperties = {
      borderTop: index === 0 ? 'none' : undefined,
      borderLeft: 'none',
      borderRight: 'none',
      paddingLeft: 0,
      paddingRight: 0,
    };

    // Parent followed by sub-answer: no bottom border
    if (isFollowedBySubAnswer) {
      return { ...baseStyle, borderBottom: 'none', paddingBottom: 0 };
    }

    // Sub-answer styling
    if (item.isSubAnswer) {
      const isLastSubAnswer = !nextItem?.isSubAnswer;
      return {
        ...baseStyle,
        borderBottom: isLastItem ? 'none' : isLastSubAnswer ? '1px solid #b1b4b6' : 'none',
        paddingTop: isFirstSubAnswer ? '10px' : '5px',
        paddingBottom: isLastSubAnswer ? undefined : '5px',
      };
    }

    return {
      ...baseStyle,
      borderBottom: isLastItem ? 'none' : '1px solid #b1b4b6',
    };
  };

  const getColumnStyle = (): React.CSSProperties => {
    if (halfWidthColumns) return { width: '50%' };
    if (equalColumns) return { width: '50%' };
    return {};
  };

  const columnStyle = getColumnStyle();

  // Per the GOV.UK summary-list spec: when any row in the list has an action, actionless rows need
  // govuk-summary-list__row--no-actions so their borders and column widths stay aligned.
  const hasAnyAction = items.some((item) => item.onAction || item.actionTo);

  return (
    <dl className={listClassName} style={noOuterBorder ? { border: 'none', marginTop: 0 } : {}} data-testid={testId}>
      {items.map((item, index) => {
        const rowTestId = item.testId ?? composeTestId(testId, 'row', slugifyTestId(item.label) || String(index));
        const rowClassName = ['govuk-summary-list__row', hasAnyAction && !item.onAction && !item.actionTo ? 'govuk-summary-list__row--no-actions' : ''].filter(Boolean).join(' ');
        return (
          <div className={rowClassName} key={index} style={{ ...getRowStyle(index, item), paddingLeft: 0, paddingRight: 0 }} data-testid={rowTestId}>
            <dt
              className="govuk-summary-list__key"
              style={{
                ...columnStyle,
                fontWeight: item.isSubAnswer ? 400 : 700,
              }}
              data-testid={composeTestId(rowTestId, 'key')}>
              {item.isSubAnswer && !plainSubAnswers ? (
                <>
                  <span aria-hidden="true">— </span>
                  {item.label}
                </>
              ) : (
                item.label
              )}
            </dt>
            {item.value !== undefined && (
              <dd className="govuk-summary-list__value" style={columnStyle} data-testid={composeTestId(rowTestId, 'value')}>
                {item.value}
              </dd>
            )}
            {!item.onAction && item.actionTo && (
              <dd className="govuk-summary-list__actions">
                <RouterLink className="govuk-link" to={item.actionTo} state={item.actionState} data-testid={composeTestId(rowTestId, 'action')}>
                  {item.actionLabel}
                  <span className="govuk-visually-hidden"> {item.label}</span>
                </RouterLink>
              </dd>
            )}
            {item.onAction && (
              <dd className="govuk-summary-list__actions">
                <button type="button" className="govuk-link" style={{ font: 'inherit' }} data-testid={composeTestId(rowTestId, 'action')} onClick={() => item.onAction?.()}>
                  {item.actionLabel || 'Change'}
                  <span className="govuk-visually-hidden"> {item.label.toLowerCase()}</span>
                </button>
              </dd>
            )}
          </div>
        );
      })}
    </dl>
  );
}

export default SummaryList;
