import React from 'react';
import { GovUKSummaryCard, SummaryList } from '../../../GovUKComponents';
import { datePartsValid, formVersionShown, isoFromParts, type CollectionFormState } from '../../../../views/Admin/Collections/reducer';
import type { CollectionFormOptionDto, ReportingLaDto } from '../../../../views/Admin/Collections/queries';
import { formatGovUkDate, quarterLabel, typeLabel } from '../../../../views/Admin/Collections/display';
import useFeature from '../../../../hooks/useFeature';

// Step indices the "Change" links jump back to (mirrors the wizard's step order):
// 0 details, 1 window, 2 forms, 3 reporting LAs, 4 reminders.
const STEP = {
  details: 0,
  window: 1,
  forms: 2,
  reporting: 3,
  reminders: 4,
} as const;

interface Props {
  state: CollectionFormState;
  onChange: (stepIndex: number) => void;
  formOptions: CollectionFormOptionDto[];
  reportingLas: ReportingLaDto[];
}

const TEST_ID = 'admin-collections-create-check';

// Renders an em-dash for empty values so every row matches the View page's "—" convention.
const orDash = (value: string): string => value || '—';

// Stacks a list of names one per line (matching the View page's <br/>-separated value cells).
const stackedNames = (names: string[]): React.ReactNode =>
  names.length > 0 ? (
    <>
      {names.map((name, index) => (
        <React.Fragment key={index}>
          {index > 0 && <br />}
          {name}
        </React.Fragment>
      ))}
    </>
  ) : (
    '—'
  );

const formatDateParts = (day: string, month: string, year: string): string => {
  if (!datePartsValid(day, month, year)) {
    return '—';
  }
  return formatGovUkDate(isoFromParts(day, month, year));
};

const StepCheckAnswers = ({ state, onChange, formOptions, reportingLas }: Props): React.ReactElement => {
  const versionsEnabled = useFeature('BLUM_9087');
  const sameWindow = state.windowSameAsReportingPeriod === 'yes';
  const allLas = state.isSubmittedByAllLas === 'yes';
  const quarter = quarterLabel(state.quarter);

  // Card 1 — Data collection details.
  const detailItems: { label: string; value: React.ReactNode }[] = [
    { label: 'What is the name of this data collection?', value: orDash(state.name) },
    { label: 'What is the purpose of this data collection? (optional)', value: orDash(state.purpose) },
    { label: 'What is the type of this data collection?', value: state.type ? typeLabel(state.type) : '—' },
    { label: 'Financial year', value: orDash(state.financialYear) },
    ...(state.type === 'quarterly_report' ? [{ label: 'Quarter', value: quarter }] : []),
  ];

  const windowItems: { label: string; value: React.ReactNode }[] = [
    { label: 'Is the data collection window the same as the reporting period?', value: state.windowSameAsReportingPeriod ? (sameWindow ? 'Yes' : 'No') : '—' },
  ];

  if (!sameWindow && state.windowSameAsReportingPeriod === 'no') {
    windowItems.push(
      { label: 'When will the data collection open?', value: formatDateParts(state.opensDay, state.opensMonth, state.opensYear) },
      { label: 'What is the deadline for submissions?', value: formatDateParts(state.deadlineDay, state.deadlineMonth, state.deadlineYear) }
    );
  }

  // Card 3 — Forms (selected module names, stacked one per line).
  // With versions offered (BLUM-9103), each versioned form also says which version the collection will use.
  const formLabel = (option: CollectionFormOptionDto): string => {
    if (!versionsEnabled || (option.versions?.length ?? 0) === 0) {
      return option.name;
    }
    const version = formVersionShown(state, option.moduleId);
    return version ? `${option.name} (version ${version})` : `${option.name} (latest version)`;
  };
  const selectedFormNames = state.formModuleIds
    .map((id) => formOptions.find((option) => option.moduleId === id))
    .filter((option): option is CollectionFormOptionDto => Boolean(option))
    .map(formLabel);

  // Card 4 — Reporting organisations.
  const selectedLaNames = reportingLas.filter((la) => state.selectedLaIds.includes(la.id)).map((la) => la.name);
  const reportingItems: { label: string; value: React.ReactNode }[] = [
    { label: 'Are you requesting data from all organisations?', value: state.isSubmittedByAllLas ? (allLas ? 'Yes' : 'No') : '—' },
  ];

  if (!allLas && state.isSubmittedByAllLas === 'no') {
    reportingItems.push({ label: 'Which organisations must submit information?', value: stackedNames(selectedLaNames) });
  }

  // Card 5 — Automatic reminders (one row per configured reminder, or a single em-dash row when none).
  // Row labels are "Reminder 1"/"Reminder 2" (or plain "Reminder" for a single one) — matching
  // CollectionReminderBlock's own headings — rather than repeating the question text, which gave every
  // row the same label with no way to tell which answer belonged to which reminder.
  const configuredReminders = state.reminders.filter((reminder) => reminder.offsetType);
  // "More than one" is keyed off the raw reminders array, matching reducer.ts's reminderSuffix and
  // CollectionReminderBlock's `total` prop, not the filtered configuredReminders count below.
  const reminderLabel = (index: number): string => (state.reminders.length > 1 ? `Reminder ${index + 1}` : 'Reminder');
  const reminderItems =
    configuredReminders.length > 0
      ? configuredReminders.map((reminder, index) => ({
          label: reminderLabel(index),
          value: reminder.offsetType === 'after_open' ? `${reminder.days || '0'} days after the collection opens` : `${reminder.days || '0'} days before the submission deadline`,
          testId: `${TEST_ID}-reminders-row-${index}`,
        }))
      : [{ label: 'Reminder', value: '—' }];

  return (
    <div data-testid={TEST_ID}>
      <GovUKSummaryCard
        title="Data collection details"
        testId={`${TEST_ID}-details`}
        actions={[{ label: 'Edit', onClick: () => onChange(STEP.details), visuallyHiddenText: 'data collection details' }]}>
        <SummaryList items={detailItems} equalColumns />
      </GovUKSummaryCard>

      <GovUKSummaryCard
        title="Collection window"
        testId={`${TEST_ID}-window`}
        actions={[{ label: 'Edit', onClick: () => onChange(STEP.window), visuallyHiddenText: 'collection window' }]}>
        <SummaryList items={windowItems} equalColumns />
      </GovUKSummaryCard>

      <GovUKSummaryCard title="Forms" testId={`${TEST_ID}-forms`} actions={[{ label: 'Edit', onClick: () => onChange(STEP.forms), visuallyHiddenText: 'forms' }]}>
        <SummaryList items={[{ label: 'Which forms do you want to include in this data collection?', value: stackedNames(selectedFormNames) }]} equalColumns />
      </GovUKSummaryCard>

      <GovUKSummaryCard
        title="Reporting organisations"
        testId={`${TEST_ID}-reporting`}
        actions={[{ label: 'Edit', onClick: () => onChange(STEP.reporting), visuallyHiddenText: 'reporting organisations' }]}>
        <SummaryList items={reportingItems} equalColumns />
      </GovUKSummaryCard>

      <GovUKSummaryCard
        title="Automatic reminders"
        testId={`${TEST_ID}-reminders`}
        actions={[{ label: 'Edit', onClick: () => onChange(STEP.reminders), visuallyHiddenText: 'automatic reminders' }]}>
        <SummaryList items={reminderItems} equalColumns />
      </GovUKSummaryCard>
    </div>
  );
};

export default StepCheckAnswers;
