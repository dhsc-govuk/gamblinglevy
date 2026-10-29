import React from 'react';
import { GovUKSummaryCard, SummaryList, isQuestionVisible } from '../../../GovUKComponents';
import type { ActivityFormState } from '../../../../views/Activities/reducer';
import type { ActivityTaxonomyDto } from '../../../../views/Activities/queries';
import type { FormQuestionDto, FormSectionDto } from '../../../../types/formSchema';
import { resolveAnswerDisplay } from '../../../../views/Activities/answers';
import {
  ACTIVITY_NAME_QUESTION,
  ADDITIONAL_INFORMATION_SECTION,
  activityCopy,
  activityTypeDisplayName,
  CATEGORY_QUESTION,
  EXPECTED_RESULTS_QUESTION,
  OTHER_MEASURE_LABEL,
  formatStartDate,
  isOtherType,
  keyResultsForTask,
  measureRowLabel,
  STATUS_LABELS,
} from '../../../../views/Activities/display';

export interface AnswerSection {
  section: FormSectionDto;
  questions: FormQuestionDto[];
  stepIndex: number;
  showNameField?: boolean;
}

interface Props {
  state: ActivityFormState;
  taxonomy?: ActivityTaxonomyDto;
  isPeople: boolean;
  answerSections: AnswerSection[];
  categoryStepIndex: number | null;
  typeStepIndex: number | null;
  expectedResultsStepIndex: number | null;
  // Null where there is no status step to change (a fixed status).
  statusStepIndex: number | null;
  // False for a category offering exactly one activity type: the user was never asked, so summarising an answer
  // they did not give — and repeating the category name a second time — is noise (BLUM-7860).
  showTypeCard?: boolean;
  // True where the status is fixed rather than chosen (forcedActivityStatus): the card then shows the status
  // read-only — no Change link and no start date, neither of which was asked (BLUM-8090).
  statusLocked?: boolean;
  additionalInformationPlacement?: 'before-status' | 'after-status';
  onChange: (step: number) => void;
}

const StepCheckAnswers = ({
  state,
  taxonomy,
  isPeople,
  answerSections,
  categoryStepIndex,
  typeStepIndex,
  expectedResultsStepIndex,
  statusStepIndex,
  showTypeCard = true,
  statusLocked = false,
  additionalInformationPlacement = 'after-status',
  onChange,
}: Props): React.ReactElement => {
  const categoryName = taxonomy?.categories.find((category) => category.id === state.categoryId)?.name ?? 'Not provided';
  const copy = activityCopy(isPeople, categoryName);
  const task = taxonomy?.tasks.find((item) => item.id === state.taskId);
  const taskName = task ? activityTypeDisplayName(task.name) : 'Not provided';
  const otherSelected = task ? isOtherType(task.name) : false;

  const inlineAnswer = (values: string[]): string => (values.length > 0 ? values.join(', ') : 'Not provided');
  const standardKeyResults = state.keyResultCodes.map((code) => taxonomy?.keyResults.find((keyResult) => keyResult.code === code)?.label ?? code);
  const customKeyResults = state.expectedResults.map((text) => text.trim()).filter(Boolean);
  const hasStandardKeyResultOptions = !taxonomy || keyResultsForTask(taxonomy, state.taskId).length > 0;
  const expectedResultItems = hasStandardKeyResultOptions
    ? [
        {
          label: EXPECTED_RESULTS_QUESTION,
          value: inlineAnswer([...standardKeyResults, ...(customKeyResults.length > 0 ? ['Other'] : [])]),
        },
        ...(customKeyResults.length > 0 ? [{ label: OTHER_MEASURE_LABEL, value: inlineAnswer(customKeyResults) }] : []),
      ]
    : customKeyResults.map((value, index) => ({ label: measureRowLabel(index), value }));
  const startDisplay = formatStartDate(state.startMonth, state.startYear) ?? 'Not provided';

  const typeItems = [{ label: copy.typeQuestion, value: taskName }];
  if (otherSelected) {
    typeItems.push({ label: copy.otherLabel, value: state.customTaskText || 'Not provided' });
  }

  const statusItems = [{ label: copy.statusQuestion, value: state.status ? STATUS_LABELS[state.status as keyof typeof STATUS_LABELS] : 'Not provided' }];
  if (!statusLocked && state.status === 'active') {
    statusItems.push({ label: copy.startQuestion, value: startDisplay });
  }
  if (!statusLocked && state.status === 'closed') {
    statusItems.push({ label: copy.closureQuestion, value: state.closureReason.trim() || 'Not provided' });
  }

  const renderAnswerSection = ({ section, questions, stepIndex, showNameField }: AnswerSection): React.ReactNode => {
    const visibleQuestions = questions.filter((question) => question.isActive && isQuestionVisible(question, state.answers));
    if (visibleQuestions.length === 0 && !showNameField) {
      return null;
    }

    const nameItem = showNameField ? [{ label: ACTIVITY_NAME_QUESTION, value: state.title.trim() || 'Not provided' }] : [];
    // Cards keep their real schema section title — Education/Help have two detail sections (Activity details
    // and People and issues, BLUM-7120), so a blanket detail-card title would render duplicates.
    const sectionTitle = section.title;

    return (
      <GovUKSummaryCard
        key={section.id}
        title={sectionTitle}
        testId={`activities-check-section-${section.id}`}
        actions={[{ label: 'Change', onClick: () => onChange(stepIndex), visuallyHiddenText: sectionTitle }]}>
        <SummaryList
          halfWidthColumns
          testId={`activities-check-section-${section.id}-answers`}
          items={[
            ...nameItem,
            ...visibleQuestions.map((question) => ({
              label: question.label,
              value: inlineAnswer(resolveAnswerDisplay(question, state.answers[question.code])),
            })),
          ]}
        />
      </GovUKSummaryCard>
    );
  };

  // The create and edit designs share the same card renderer but place Additional information differently;
  // the caller supplies the edit-only placement override.
  const detailSections = answerSections.filter(({ section }) => section.title !== ADDITIONAL_INFORMATION_SECTION);
  const additionalInformationSections = answerSections.filter(({ section }) => section.title === ADDITIONAL_INFORMATION_SECTION);

  return (
    <>
      <GovUKSummaryCard
        title="Activity category"
        testId="activities-check-category"
        actions={categoryStepIndex !== null ? [{ label: 'Change', onClick: () => onChange(categoryStepIndex), visuallyHiddenText: 'activity category' }] : []}>
        <SummaryList halfWidthColumns items={[{ label: CATEGORY_QUESTION, value: categoryName }]} />
      </GovUKSummaryCard>

      {showTypeCard && (
        <GovUKSummaryCard
          title={copy.typeTitle}
          testId="activities-check-type"
          actions={typeStepIndex !== null ? [{ label: 'Change', onClick: () => onChange(typeStepIndex), visuallyHiddenText: 'activity type' }] : []}>
          <SummaryList halfWidthColumns items={typeItems} />
        </GovUKSummaryCard>
      )}

      {detailSections.map(renderAnswerSection)}

      {expectedResultsStepIndex !== null && (
        <GovUKSummaryCard
          title="Expected results"
          testId="activities-check-expected-results"
          actions={[{ label: 'Change', onClick: () => onChange(expectedResultsStepIndex), visuallyHiddenText: 'expected results' }]}>
          <SummaryList halfWidthColumns testId="activities-check-expected-results-answers" items={expectedResultItems} />
        </GovUKSummaryCard>
      )}

      {additionalInformationPlacement === 'before-status' && additionalInformationSections.map(renderAnswerSection)}

      <GovUKSummaryCard
        title={copy.statusTitle}
        testId="activities-check-status"
        actions={statusStepIndex !== null ? [{ label: 'Change', onClick: () => onChange(statusStepIndex), visuallyHiddenText: 'activity status' }] : []}>
        <SummaryList halfWidthColumns testId="activities-check-status-answers" items={statusItems} />
      </GovUKSummaryCard>

      {additionalInformationPlacement === 'after-status' && additionalInformationSections.map(renderAnswerSection)}
    </>
  );
};

export default StepCheckAnswers;
