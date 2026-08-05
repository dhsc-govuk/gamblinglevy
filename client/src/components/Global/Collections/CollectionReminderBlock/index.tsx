import React from 'react';
import { GovUKButton, GovUKFieldset, GovUKInputWithSuffix, H3 } from '../../../GovUKComponents';

type ReminderOffset = '' | 'after_open' | 'before_deadline';

interface ReminderValue {
  id: string;
  offsetType: ReminderOffset;
  days: string;
}

interface ReminderError {
  offsetType?: string;
  days?: string;
}

interface CollectionReminderBlockProps {
  index: number;
  // Total number of reminders currently configured — drives the "Reminder"/"Reminder N" heading and
  // whether the Remove button shows at all (per Figma, Remove is hidden while there's only one reminder).
  total: number;
  reminder: ReminderValue;
  onChange: (patch: Partial<Omit<ReminderValue, 'id'>>) => void;
  onRemove: () => void;
  error?: ReminderError;
  testId?: string;
}

// One reminder in the add-collection wizard's "Reminders" step: choose whether the reminder fires a
// number of days after the collection opens or before the submission deadline.
const CollectionReminderBlock = ({ index, total, reminder, onChange, onRemove, error, testId }: CollectionReminderBlockProps): React.ReactElement => {
  const root = testId ?? `admin-collections-create-reminder-${index}`;
  const renderDaysInput = (offsetType: Exclude<ReminderOffset, ''>) => {
    const isAfterOpen = offsetType === 'after_open';
    return (
      <div className="collection-wizard__reminder-days">
        <GovUKInputWithSuffix
          id={`${root}-${offsetType}-days`}
          name={`${root}-${offsetType}-days`}
          label={
            isAfterOpen
              ? 'How many days after the collection opens do you want to send the reminder?'
              : 'How many days before the submission deadline do you want to send the reminder?'
          }
          hint={isAfterOpen ? 'Enter ‘0’ if you want to send the reminder on the first day' : 'Enter ‘0’ if you want to send the reminder on the last day'}
          suffix="days"
          value={reminder.days}
          error={error?.days}
          width="2"
          type="number"
          onChange={(event) => onChange({ days: event.target.value })}
          testId={`${root}-${offsetType}-days`}
        />
      </div>
    );
  };

  return (
    <div className="collection-wizard__reminder" data-testid={root}>
      <div className="collection-wizard__reminder-header">
        <H3>{total > 1 ? `Reminder ${index + 1}` : 'Reminder'}</H3>
        {total > 1 && (
          <GovUKButton className="govuk-button govuk-button--secondary" onClick={onRemove} data-testid={`${root}-remove`}>
            Remove
          </GovUKButton>
        )}
      </div>

      <GovUKFieldset.RadioGroup
        name={`${root}-trigger`}
        legend="When do you want to send the reminder?"
        legendSize="s"
        value={reminder.offsetType || undefined}
        error={error?.offsetType}
        size="small"
        onChange={(value) => onChange({ offsetType: value as ReminderOffset })}
        options={[
          {
            value: 'after_open',
            text: 'A number of days after the collection opens',
            conditionalContent: reminder.offsetType === 'after_open' ? renderDaysInput('after_open') : undefined,
          },
          {
            value: 'before_deadline',
            text: 'A number of days before the submission deadline',
            conditionalContent: reminder.offsetType === 'before_deadline' ? renderDaysInput('before_deadline') : undefined,
          },
        ]}
      />
    </div>
  );
};

export default CollectionReminderBlock;
