import React from 'react';
import { GovUKDynamicQuestionRenderer, GovUKFieldset, Paragraph } from '../../../GovUKComponents';
import type { FormQuestionDto, FormSectionDto } from '../../../../types/formSchema';
import type { ActivityFormState, ActivityFormAction } from '../../../../views/Activities/reducer';
import { ACTIVITY_NAME_QUESTION } from '../../../../views/Activities/display';

interface Props {
  section: FormSectionDto;
  questions: FormQuestionDto[];
  state: ActivityFormState;
  dispatch: React.Dispatch<ActivityFormAction>;
  showNameField?: boolean;
  // The selected type-of-work label (or custom "Other" text) — interpolated into the name-field hint.
  selectedTypeName?: string;
}

// Renders one category-scoped form section's questions as a step in the activity wizard. The section title is
// rendered as the page heading by the wizard host; this body renders the optional description + the questions.
// Conditional reveal and active-question filtering are handled by GovUKDynamicQuestionRenderer.
const StepDynamicSection = ({ section, questions, state, dispatch, showNameField = false, selectedTypeName }: Props): React.ReactElement => {
  const activeQuestions = questions
    .filter((question) => question.isActive)
    .map((question) => ({
      ...question,
      label:
        question.code.endsWith('_RELATED_ISSUES_OTHER')
          ? 'What else will the activity address?'
          : question.code === 'PR_FUNDING_TIME'
            ? question.label.replace("staff member's", 'staff member’s')
            : question.label,
      hint:
        section.title === 'Additional information' && question.code.endsWith('_PLAN')
          ? "Use this space to add any other information about your approach and the steps you'll take"
          : question.hint,
      options:
        question.code === 'PR_FUNDING_TIME'
          ? question.options.map((option) => ({
              ...option,
              label: ['21_40', '41_60', '61_80'].includes(option.value) ? option.label.replace(' to ', '% to ') : option.label,
            }))
          : question.options,
    }));
  const nameHint = selectedTypeName ? (
    <>
      If you do not enter a name, we’ll use the type of work you selected:
      <span className="govuk-!-display-block">{selectedTypeName}</span>
    </>
  ) : (
    'If you do not enter a name, we’ll use the type of work you selected'
  );

  return (
    <>
      {section.description && <Paragraph>{section.description}</Paragraph>}
      {showNameField && (
        <GovUKFieldset.Input
          id="activity-name"
          name="activity-name"
          testId="activities-details-activity-name"
          label={ACTIVITY_NAME_QUESTION}
          labelSize="m"
          hint={nameHint}
          value={state.title}
          error={state.errors['activity-name']}
          onChange={(e) => dispatch({ type: 'SET_TITLE', value: e.target.value })}
        />
      )}
      {activeQuestions.length > 0 ? (
        <GovUKDynamicQuestionRenderer
          questions={activeQuestions}
          answers={state.answers}
          errors={state.errors}
          controlSize="small"
          questionSize="m"
          showQuestionCode={false}
          onAnswerChange={(code, value) => dispatch({ type: 'SET_ANSWER', code, value })}
        />
      ) : (
        !showNameField && <Paragraph>There are no questions in this section.</Paragraph>
      )}
    </>
  );
};

export default StepDynamicSection;
