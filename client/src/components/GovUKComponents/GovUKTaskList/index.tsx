import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import type { TagColour } from '../GovUKTag';
import GovUKTag from '../GovUKTag';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import './styles.css';

export interface TaskListItemStatus {
  // Keep short. The status renders into a nowrap table cell, so this column cannot compress: a long
  // label steals width from the task name rather than wrapping (BLUM-7045).
  text: string;
  // Renders the status text dimmed (GOV.UK "Cannot start yet" treatment) for tasks not yet available.
  cannotStartYet?: boolean;
  tag?: {
    text: string;
    colour?: TagColour;
  };
}

export interface TaskListItem {
  id: string;
  title: string;
  hint?: string;
  href?: string;
  status: TaskListItemStatus;
  onClick?: () => void;
  testId?: string;
}

interface GovUKTaskListProps {
  items: TaskListItem[];
  idPrefix?: string;
  testId?: string;
}

function GovUKTaskList({ items, idPrefix = 'task-list', testId }: GovUKTaskListProps): React.ReactElement {
  return (
    <ul className="govuk-task-list" data-testid={testId}>
      {items.map((item, index) => {
        const statusId = `${idPrefix}-${index}-status`;
        const hintId = item.hint ? `${idPrefix}-${index}-hint` : undefined;
        const hasLink = !!item.href || !!item.onClick;
        const ariaDescribedBy = [hintId, statusId].filter(Boolean).join(' ');
        const itemTestId = item.testId ?? composeTestId(testId, 'item', slugifyTestId(item.title) || item.id || String(index));

        return (
          <li key={item.id} className={`govuk-task-list__item${hasLink ? ' govuk-task-list__item--with-link' : ''}`} data-testid={itemTestId}>
            <div className="govuk-task-list__name-and-hint">
              {hasLink ? (
                item.href ? (
                  <RouterLink
                    to={item.href}
                    className="govuk-link govuk-task-list__link"
                    aria-describedby={ariaDescribedBy}
                    data-testid={composeTestId(itemTestId, 'link')}
                    onClick={(e) => {
                      if (item.onClick) {
                        e.preventDefault();
                        item.onClick();
                      }
                    }}>
                    {item.title}
                  </RouterLink>
                ) : (
                  <button
                    type="button"
                    className="govuk-link govuk-task-list__link"
                    aria-describedby={ariaDescribedBy}
                    data-testid={composeTestId(itemTestId, 'link')}
                    onClick={item.onClick}>
                    {item.title}
                  </button>
                )
              ) : (
                <span aria-describedby={ariaDescribedBy}>{item.title}</span>
              )}
              {item.hint && (
                <div id={hintId} className="govuk-task-list__hint">
                  {item.hint}
                </div>
              )}
            </div>
            <div
              className={`govuk-task-list__status${item.status.cannotStartYet ? ' govuk-task-list__status--cannot-start-yet' : ''}`}
              id={statusId}
              data-testid={composeTestId(itemTestId, 'status')}>
              {/* What stops the label breaking across lines is white-space: nowrap on
                  .govuk-task-list__status, which inherits into this tag — NOT this modifier.
                  --nowrap only lifts govuk-frontend's max-width: 160px on .govuk-tag, so that a
                  label wider than 160px overflows nothing rather than being clamped with its
                  un-wrappable text spilling out of the coloured box. Inert for every label in use
                  today (the widest is ~110px); it is here so the two rules can't disagree. */}
              {item.status.tag ? (
                <GovUKTag colour={item.status.tag.colour} className="govuk-tag--nowrap">
                  {item.status.tag.text}
                </GovUKTag>
              ) : (
                item.status.text
              )}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

export default GovUKTaskList;
