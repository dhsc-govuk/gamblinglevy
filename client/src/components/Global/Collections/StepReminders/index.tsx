import React, { useEffect } from 'react';
import { GovUKButton, Paragraph } from '../../../GovUKComponents';
import CollectionReminderBlock from '../CollectionReminderBlock';
import type { CollectionFormState, CollectionFormAction } from '../../../../views/Admin/Collections/reducer';

interface Props {
  state: CollectionFormState;
  dispatch: React.Dispatch<CollectionFormAction>;
}

// "Automatic reminders" step of the add-collection wizard (Figma).
const StepReminders = ({ state, dispatch }: Props): React.ReactElement => {
  useEffect(() => {
    dispatch({ type: 'ENSURE_REMINDER' });
  }, [dispatch]);

  return (
    <>
      <Paragraph>An email will be sent to all reporting organisations that have not yet submitted their information. You can add more than one reminder.</Paragraph>

      {state.reminders.map((reminder, index) => (
        <CollectionReminderBlock
          key={reminder.id}
          index={index}
          total={state.reminders.length}
          reminder={reminder}
          onChange={(patch) => dispatch({ type: 'SET_REMINDER', id: reminder.id, patch })}
          onRemove={() => dispatch({ type: 'REMOVE_REMINDER', id: reminder.id })}
          error={{
            offsetType: state.errors[`reminder-${index}-trigger`],
            days: state.errors[`reminder-${index}-days`],
          }}
          testId={`admin-collections-create-reminder-${index}`}
        />
      ))}

      <div className="collection-wizard__reminder-actions">
        <GovUKButton className="govuk-button govuk-button--secondary" onClick={() => dispatch({ type: 'ADD_REMINDER' })} data-testid="admin-collections-create-add-reminder">
          Add another reminder
        </GovUKButton>
      </div>
    </>
  );
};

export default StepReminders;
