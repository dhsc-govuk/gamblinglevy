import React from 'react';
import { isQuestionVisible, type TaskListItemStatus } from '../../components/GovUKComponents';
import type { FormQuestionDto } from '../../types/formSchema';
import { formatGovUkDate } from '../Admin/Collections/display';
import { SUBMISSION_STATUS_LABEL, SUBMISSION_STATUS_TAG_COLOUR } from '../Admin/Reports/display';
import type { SubmissionStatus } from '../Admin/Reports/queries';
import { resolveAnswerDisplay } from '../Activities/answers';
import { renderAnswerDisplay } from '../Activities/display';
import type { CollectionType } from '../Admin/Collections/queries';
import { ROUTES } from '../../helpers/routes';
import { financialYearEndYear } from '../../helpers/financialYear';
import { maturityAssessmentName } from '../MaturityAssessment/names';
import type { MyCollectionSummaryDto, ReportAnswerDto, ReportProgressStatus, ReportQuestionDto } from './queries';

// Shared quarterly-report presentation/copy helpers — single source of truth for the reporting-period
// label, status-to-task-list mappings and notification-banner copy used across the report pages.

// Date/type formatters are shared with the data-collection screens; re-exported here so the LA Reports
// list/detail import them from one place.
export { formatGovUkDate, formatDateTime, typeLabel } from '../Admin/Collections/display';

// The two-line "Quarter 2 / (1 July 2026 to 30 September 2026)" summary-list value, shared with the DHSC screens.
// Re-exported so the LA pages take their display helpers from this one module rather than reaching into two
// separate Admin folders.
export { reportingPeriodValue } from '../Admin/Reports/display';

// "Quarterly report for Quarter 3 (1 July 2026 to 30 September 2026)" — the report's display name on the LA
// submission-history list and detail, composed to match the designs regardless of the admin-entered collection name.
export const reportName = (quarter: number | null, periodStart: string | null, periodEnd: string | null): string => {
  const period = `${formatGovUkDate(periodStart)} to ${formatGovUkDate(periodEnd)}`;
  return quarter == null ? `Quarterly report (${period})` : `Quarterly report for Quarter ${quarter} (${period})`;
};

// A collection row's display name on the LA screens, by type (BLUM-7859): a maturity window is "Maturity assessment
// for {end year}", a quarterly one the reportName above.
export const collectionDisplayName = (collection: Pick<MyCollectionSummaryDto, 'type' | 'quarter' | 'reportingPeriodStart' | 'reportingPeriodEnd' | 'financialYear'>): string =>
  collection.type === 'maturity_assessment'
    ? maturityAssessmentName(financialYearEndYear(collection.financialYear ?? ''))
    : reportName(collection.quarter, collection.reportingPeriodStart, collection.reportingPeriodEnd);

// Where an open, unsubmitted row on the landing takes the user: each type has its own collection-scoped flow.
export const collectionAuthoringHref = (collection: Pick<MyCollectionSummaryDto, 'type' | 'collectionId'>): string =>
  collection.type === 'maturity_assessment' ? ROUTES.reports.maturity(collection.collectionId) : ROUTES.reports.quarterly(collection.collectionId);

// "17 January 2027, 14:23" — the submission detail's "Submitted on" format per the design (Figma 420-80777):
// 24-hour clock with a comma, unlike the shared formatDateTime's "at 2:05pm" used on the DHSC screens.
export const formatSubmittedDateTime = (iso: string | null): string => {
  if (!iso) {
    return '—';
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  const time = `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
  return `${date.getDate()} ${date.toLocaleString('en-GB', { month: 'long' })} ${date.getFullYear()}, ${time}`;
};

// Progress display per the designs: not_started blue tag, in_progress yellow tag, completed plain text.
// Keep these labels short. GovUKTaskList renders the status into a nowrap table cell, so the column
// cannot compress: a long label steals width from the task name rather than wrapping (BLUM-7045).
export const progressTaskStatus = (status: ReportProgressStatus): TaskListItemStatus => {
  switch (status) {
    case 'completed':
      return { text: 'Completed' };
    case 'in_progress':
      return { text: 'In progress', tag: { text: 'In progress', colour: 'yellow' } };
    default:
      return { text: 'Not started', tag: { text: 'Not started', colour: 'blue' } };
  }
};

// Sections stay dimmed and unlinked until the activities list is confirmed.
export const CANNOT_START_YET_STATUS: TaskListItemStatus = { text: 'Cannot start yet', cannotStartYet: true };

// LA-facing wording for the derived per-organisation submission status. A finalised report reads "Submitted" to the
// LA that filed it; the DHSC screens keep "Completed" (SUBMISSION_STATUS_LABEL) — their status filter and the
// submissions CSV export are written against that label. Only the one string differs, so the rest are spread rather
// than restated. Tag colours are shared with the DHSC screens.
export const LA_SUBMISSION_STATUS_LABEL: Record<SubmissionStatus, string> = {
  ...SUBMISSION_STATUS_LABEL,
  completed: 'Submitted',
};

// A collection row's status on the Reports landing. Per the designs this uses the GOV.UK task-list statuses: an
// unsubmitted report reads "Incomplete" (blue) whether or not work has started; a submitted one keeps the LA
// wording "Submitted". 'overdue' occurs in the landing's "Overdue reports" section (BLUM-7077) and takes the
// shared red tag.
export const submissionTaskStatus = (status: SubmissionStatus): TaskListItemStatus => {
  if (status === 'not_started' || status === 'in_progress') {
    return { text: 'Incomplete', tag: { text: 'Incomplete', colour: 'blue' } };
  }
  const label = LA_SUBMISSION_STATUS_LABEL[status];
  return { text: label, tag: { text: label, colour: SUBMISSION_STATUS_TAG_COLOUR[status] } };
};

// One row of the LA report history, flattened for the table: GovUKTable's search/sort run over raw record fields,
// so the composed display name and the reporting-period sort key are precomputed here rather than only rendered.
export interface ReportHistoryRow {
  collectionId: string;
  displayName: string;
  type: CollectionType | null;
  status: SubmissionStatus;
  // Both date fields are sort keys, and BOTH must be plain strings — never null. GovUKTable's comparator only
  // orders a pair when both sides are strings and otherwise returns 0, which is not a strict weak ordering: with
  // a single null in the column, Array.prototype.sort scatters the *dated* rows too, not just the undated ones.
  // '' keeps the ordering total (ISO-8601 sorts lexicographically) and still renders as an em dash, because
  // formatGovUkDate treats any falsy value as "no date".
  reportingPeriodStart: string;
  submittedAt: string;
}

// The LA report history: the organisation's finished reporting periods — everything it submitted, plus every window
// whose deadline passed with nothing submitted. Currently-open windows live on the Reports landing (they are still
// actionable) and scheduled ones have not opened yet, so neither belongs here. An overdue period MUST appear — it
// was filtered out of both LA screens, leaving a missed deadline invisible anywhere in the service (BLUM-7077).
//
// Deliberately keyed on the derived submission status rather than `collectionStatus === 'closed'`: the two diverge
// when DHSC closes a window early (Collection.GetStatus() closes on IsManuallyClosed too, while Derive only returns
// overdue past the deadline), and such a period is neither submitted nor overdue — listing it under a heading that
// says these reports can no longer be edited, tagged an active "In progress", would state something untrue.
export const reportHistoryRows = (collections: MyCollectionSummaryDto[]): ReportHistoryRow[] =>
  collections
    .filter((collection) => collection.submittedAt != null || collection.submissionStatus === 'overdue')
    .map((collection) => ({
      collectionId: collection.collectionId,
      displayName: collectionDisplayName(collection),
      type: collection.type,
      status: collection.submissionStatus,
      reportingPeriodStart: collection.reportingPeriodStart ?? '',
      submittedAt: collection.submittedAt ?? '',
    }));

// Reporting periods whose deadline has passed with nothing finalised. The status is server-derived
// (SubmissionStatusRules.Derive) — never recomputed from dates on the client, so the two surfaces cannot disagree
// about what "overdue" means.
export const overdueCollections = (collections: MyCollectionSummaryDto[]): MyCollectionSummaryDto[] =>
  collections.filter((collection) => collection.submissionStatus === 'overdue');

// "Manage your quarterly report" success banner (location.state.banner).
export const TASKS_BANNER_COPY: Record<string, { title: string; body: string }> = {
  activitiesConfirmed: { title: 'Section completed', body: '‘Manage your list of activities’ is up to date.' },
  fundingCompleted: { title: 'Section completed', body: '‘Record funding spent’ is now up to date.' },
  fundingProgressSaved: { title: 'Progress saved', body: 'You can come back to ‘Record funding spent’ and continue later.' },
  feedbackCompleted: { title: 'Section completed', body: '‘Provide strategic feedback’ is up to date.' },
  feedbackProgressSaved: { title: 'Progress saved', body: 'You can come back to ‘Provide strategic feedback’ and continue later.' },
};

// "Select an activity" success banners (location.state.banner + name).
export const SELECT_BANNER_COPY: Record<string, { title: string; body: (name: string) => string }> = {
  activityCompleted: { title: 'Activity completed', body: (name) => `‘${name}’ is now up to date.` },
  progressSaved: { title: 'Progress saved', body: (name) => `You can come back to the activity ‘${name}’ and continue later.` },
};

// Reshapes a resolved report question into the FormQuestionDto shape consumed by
// GovUKDynamicQuestionRenderer, validateAnswers and collectAnswers. The report page ('results' |
// 'delivery') stands in for the section id.
export const toFormQuestion = (question: ReportQuestionDto): FormQuestionDto => ({
  id: question.id,
  sectionId: question.section,
  code: question.code,
  label: question.label,
  hint: question.hint,
  helpText: null,
  helpTextSummary: null,
  questionType: question.questionType,
  isRequired: question.isRequired,
  isActive: true,
  displayOrder: question.displayOrder,
  conditionalQuestionCode: question.conditionalQuestionCode,
  conditionalValue: question.conditionalValue,
  options: question.options.map((option, index) => ({ value: option.value, label: option.label, displayOrder: index })),
  minValue: question.minValue,
  maxValue: question.maxValue,
  suffix: question.suffix,
  errorMessage: question.errorMessage,
  maxLength: question.maxLength,
  maxLengthErrorMessage: question.maxLengthErrorMessage,
});

export const answersToRecord = (answers: ReportAnswerDto[]): Record<string, string> =>
  answers.reduce<Record<string, string>>((acc, answer) => {
    if (answer.value != null) {
      acc[answer.questionCode] = answer.value;
    }
    return acc;
  }, {});

const URL_PATTERN = /^https?:\/\//;

// Check-your-answers value: choice values resolved to option labels; Text answers that are URLs render
// as links.
export const renderReportAnswer = (question: FormQuestionDto, value: string | undefined): React.ReactNode => {
  const trimmed = value?.trim();
  if (question.questionType === 'Text' && trimmed && URL_PATTERN.test(trimmed)) {
    return (
      <a className="govuk-link" href={trimmed}>
        {trimmed}
      </a>
    );
  }
  return renderAnswerDisplay(resolveAnswerDisplay(question, value));
};

// The visible questions of one report page, as summary-list rows.
export const visibleQuestionRows = (questions: FormQuestionDto[], answers: Record<string, string>): { label: string; value: React.ReactNode }[] =>
  questions.filter((question) => isQuestionVisible(question, answers)).map((question) => ({ label: question.label, value: renderReportAnswer(question, answers[question.code]) }));
