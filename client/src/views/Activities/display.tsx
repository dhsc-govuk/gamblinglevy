import React from 'react';
import type { ActivityStatus, ActivityTaxonomyDto, ExpectedResultDto, TaxonomyKeyResultDto } from './queries';
import type { ExpectedResultInput } from './mutations';
import type { ActivityFormState } from './reducer';

// Shared activity presentation/copy helpers — single source of truth for the bits that were previously
// duplicated across the wizard, editor, check-answers, status/type steps and the view page.

export const PEOPLE_AND_ROLES = 'People and roles';

export const NEEDS_ASSESSMENT = 'Needs assessment';

export const EDUCATION_CAMPAIGNS_AND_OUTREACH = 'Education, campaigns and outreach';

export const HELP_ADVICE_AND_ACCESS = 'Help, advice and access to services';

export const ADDITIONAL_INFORMATION_SECTION = 'Additional information';

export const isPeopleCategory = (categoryName: string | null | undefined): boolean => categoryName === PEOPLE_AND_ROLES;

export const isNeedsAssessmentCategory = (categoryName: string | null | undefined): boolean => categoryName === NEEDS_ASSESSMENT;

export const isPeopleAndIssuesCategory = (categoryName: string | null | undefined): boolean =>
  categoryName === EDUCATION_CAMPAIGNS_AND_OUTREACH || categoryName === HELP_ADVICE_AND_ACCESS;

const DETAIL_SECTION_TITLES = ['People and issues', 'Role details', 'Activity details'];

// Which of the category-dependent detail screens belong in an activity flow: People and roles gets Role
// details alone; every other category gets the shared Activity details screen (protocol 1.BCDEF.1.1-1.4,
// BLUM-7120), and Education/Help additionally get People and issues (which the schema scopes to their
// "Other ..." type).
export const isActivitySectionApplicable = (sectionTitle: string, categoryName: string | null | undefined): boolean => {
  if (sectionTitle === ADDITIONAL_INFORMATION_SECTION) {
    return !isPeopleCategory(categoryName);
  }
  if (!DETAIL_SECTION_TITLES.includes(sectionTitle)) {
    return true;
  }
  if (isPeopleCategory(categoryName)) {
    return sectionTitle === 'Role details';
  }
  if (isPeopleAndIssuesCategory(categoryName)) {
    return sectionTitle === 'People and issues' || sectionTitle === 'Activity details';
  }
  return sectionTitle === 'Activity details';
};

// Every category except People-and-roles collects free-text "expected results".
export const categoryUsesExpectedResults = (categoryName: string | null | undefined): boolean => !!categoryName && !isPeopleCategory(categoryName);

// A type named "Other ..." reveals a free-text name (stored as the activity's custom task text).
export const isOtherType = (name: string): boolean => name.trim().toLowerCase().startsWith('other');

// The activity's name is collected on the "Activity details" section, and only for non-People categories —
// roles are named by their type. The wizard and the editor render the field behind this gate and validate it
// behind the same one, so the two can't drift apart.
export const sectionShowsNameField = (sectionTitle: string | null | undefined, isPeople: boolean): boolean => sectionTitle === 'Activity details' && !isPeople;

// Backend message for too few key-result selections (and the message we mirror client-side).
export const KEY_RESULTS_MIN_MESSAGE = 'Select the outcomes you will use to measure this activity';

// The category screen's question — shared by the category step, check-answers and the view page.
export const CATEGORY_QUESTION = 'Which category best describes this activity?';

export const ACTIVITY_NAME_QUESTION = 'What’s the name of this activity? (optional)';

// Minimum key-result selections (Question Protocol 3 July): checkbox screens need 1, except needs assessment
// which keeps its minimum of 2 (the protocol's "<2 selected" error trigger); the free-text-only "Other type
// of ..." screens still need 2 custom entries. No on-screen hint states these minimums — the protocol removed
// the hint copy (BLUM-6934). Mirrors ExpectedResultInputs.MinimumFor on the backend.
export const minKeyResultSelections = (hasStandardOptions: boolean, isNeedsAssessment: boolean): number => (!hasStandardOptions || isNeedsAssessment ? 2 : 1);

// The key-results question wording differs between the standard checkbox screens and the free-text screens.
export const keyResultsQuestionLabel = (_hasStandardOptions: boolean): string =>
  'What key results will you use to measure this activity?';

// Option-level hints for specific activity types (Question Protocol 3 July — 1.B.1 option B), keyed by the
// seeded type name.
export const TYPE_OPTION_HINTS: Record<string, string> = {
  'Creating a local needs assessment': 'Use this option if you’re doing a local Health or Joint strategic needs assessment, or other type of needs assessment',
};

const TYPE_DISPLAY_NAMES: Record<string, string> = {
  'Documenting a strategy and action plan': 'Documenting a formal strategy and action plan',
  'Reviewing and evolving current licensing policy': 'Reviewing and evolving licensing policy',
  'Running campaigns and workshops to raise awareness among local residents and at-risk groups':
    'Running campaigns and events to raise awareness among local residents and at-risk groups',
  'Using screening tools to better assess gambling harm': 'Using screening tools to better assess gambling harms',
  'Other type of help, advice or service': 'Other type of help, advice or access to a service',
};

export const activityTypeDisplayName = (name: string): string => TYPE_DISPLAY_NAMES[name] ?? name;

// The standard protocol key results offered for an activity type, in seed order.
export const keyResultsForTask = (taxonomy: ActivityTaxonomyDto | undefined, taskId: string): TaxonomyKeyResultDto[] =>
  (taxonomy?.keyResults ?? []).filter((keyResult) => keyResult.taskId === taskId);

// Enough key results must be chosen for the category — standard checkbox selections plus non-empty custom
// "Other" texts. When the current type has no standard options (taxonomy known), stale codes are ignored
// defensively. While the taxonomy is still loading the type's options are unknown, so the strictest minimum
// (2) applies — otherwise a free-text-only type could briefly pass with 1 and be rejected server-side.
export const validateKeyResultSelections = (
  state: Pick<ActivityFormState, 'keyResultCodes' | 'expectedResults' | 'otherExpectedResultSelected'>,
  hasStandardOptions: boolean,
  isNeedsAssessment: boolean,
  taxonomyLoaded = true
): Record<string, string> => {
  const customCount = state.expectedResults.filter((text) => text.trim()).length;
  if (hasStandardOptions && state.otherExpectedResultSelected && customCount === 0) {
    return { 'activity-expected-result-0': 'Enter what other result you will use' };
  }
  const selected = (hasStandardOptions ? state.keyResultCodes.length : 0) + customCount;
  const minimum = taxonomyLoaded ? minKeyResultSelections(hasStandardOptions, isNeedsAssessment) : 2;
  return selected >= minimum ? {} : { 'activity-expected-results': KEY_RESULTS_MIN_MESSAGE };
};

// Splits an ActivityDto's expected-result rows into form state: standard selections (codes) and custom
// free-text rows (at least one empty input so the "Other" repeater renders).
export const splitExpectedResults = (
  rows: ExpectedResultDto[]
): { keyResultCodes: string[]; expectedResults: string[]; otherExpectedResultSelected: boolean } => {
  const keyResultCodes = rows.flatMap((row) => (row.keyResultCode != null ? [row.keyResultCode] : []));
  const custom = rows.filter((row) => row.keyResultCode == null).map((row) => row.text);
  return { keyResultCodes, expectedResults: custom.length > 0 ? custom : [''], otherExpectedResultSelected: custom.length > 0 };
};

// The create/update payload rows: standard codes first (text empty — the backend snapshots the definition
// label), then trimmed non-empty custom texts.
export const buildExpectedResultsPayload = (state: Pick<ActivityFormState, 'keyResultCodes' | 'expectedResults'>): ExpectedResultInput[] => [
  ...state.keyResultCodes.map((code) => ({ keyResultCode: code, text: '' })),
  ...state.expectedResults
    .map((text) => text.trim())
    .filter(Boolean)
    .map((text) => ({ keyResultCode: null, text })),
];

// Display labels for saved expected-result rows: standard rows resolve the live taxonomy label by code
// (falling back to the snapshotted text), followed by the custom free texts.
export const expectedResultRowsDisplay = (rows: ExpectedResultDto[], taxonomy?: ActivityTaxonomyDto): string[] => {
  const standard = rows
    .filter((row) => row.keyResultCode != null)
    .map((row) => taxonomy?.keyResults.find((keyResult) => keyResult.code === row.keyResultCode)?.label ?? row.text)
    .filter(Boolean);
  const custom = rows
    .filter((row) => row.keyResultCode == null)
    .map((row) => row.text.trim())
    .filter(Boolean);
  return [...standard, ...custom];
};

// Display labels for in-progress form selections (check-your-answers): standard selections' labels followed
// by the custom free texts.
export const selectedKeyResultLabels = (state: Pick<ActivityFormState, 'keyResultCodes' | 'expectedResults'>, taxonomy?: ActivityTaxonomyDto): string[] => [
  ...state.keyResultCodes.map((code) => taxonomy?.keyResults.find((keyResult) => keyResult.code === code)?.label ?? code),
  ...state.expectedResults.map((text) => text.trim()).filter(Boolean),
];

const MONTHS = ['', 'January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

// Formats a stored start month/year ("3", 2026) as "March 2026". Returns null when either part is missing.
export const formatStartDate = (month: number | string | null | undefined, year: number | string | null | undefined): string | null => {
  if (month == null || month === '' || year == null || year === '') {
    return null;
  }
  const monthNumber = Number(month);
  const name = monthNumber >= 1 && monthNumber <= 12 ? MONTHS[monthNumber] : String(month);
  return `${name} ${year}`;
};

export const STATUS_LABELS: Record<ActivityStatus, string> = {
  planned: 'Planned',
  active: 'Active',
  closed: 'Closed',
  finished: 'Finished',
};

export const statusLabel = (status: ActivityStatus | null, isDraft: boolean): string => (isDraft || !status ? 'Draft' : STATUS_LABELS[status]);

// Renders a question/expected-results answer (which may be empty, single, or a list) for a summary value.
export const renderAnswerDisplay = (display: string[]): React.ReactNode => {
  if (display.length === 0) {
    return 'Not provided';
  }
  if (display.length === 1) {
    return display[0];
  }
  return (
    <ul className="govuk-list govuk-list--bullet">
      {display.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </ul>
  );
};

export interface ActivityCopy {
  typeTitle: string;
  typeQuestion: string;
  typeHint?: string;
  typeError: string;
  otherLabel: string;
  otherError: string;
  // Names the custom "Other" free text in its length error, after what otherLabel asks for — a role's name, but
  // an activity. Deliberately not "Activity name", which would read as the separate activity-name field.
  otherNameLabel: string;
  statusTitle: string;
  statusQuestion: string;
  statusError: string;
  startQuestion: string;
  statusHints: Record<string, string>;
}

// Role-vs-activity wording for the type/status screens, keyed off the People-and-roles category.
export const activityCopy = (isPeople: boolean, _categoryName?: string | null): ActivityCopy =>
  isPeople
    ? {
        typeTitle: 'Role type',
        typeQuestion: "What's the role?",
        typeHint: "If you've hired or plan to hire more than one person, create a new activity for each role",
        typeError: 'Select a role',
        otherLabel: "What's the name of the other role?",
        otherError: 'Enter a role',
        otherNameLabel: 'Role name',
        statusTitle: 'Role status',
        statusQuestion: "What's the status of this role?",
        statusError: 'Select the status of the role',
        startQuestion: 'When did the staff member start work?',
        statusHints: {
          planned: 'Empty and still needed',
          active: 'Filled and ongoing',
          closed: 'Ended and no longer needed',
          finished: 'All work is complete and the position is empty',
        },
      }
    : {
        typeTitle: 'Activity type',
        typeQuestion: 'What type of work does it involve?',
        typeHint: 'Create a new activity for each type',
        typeError: 'Select a type of work',
        otherLabel: "What's the activity?",
        otherError: 'Enter an activity',
        otherNameLabel: 'Activity',
        statusTitle: 'Activity status',
        statusQuestion: 'What’s the status of this activity?',
        statusError: 'Select the status of the activity',
        startQuestion: 'When did the work start?',
        statusHints: {
          planned: 'Approved but work has not started',
          active: 'Work is underway',
          closed: 'Stopped before completion',
          finished: 'All work is complete',
        },
      };

interface StartDateValidationError {
  field: 'activity-start-month' | 'activity-start-year';
  message: string;
}

// Active activities require a start month (1-12) and four-digit year. The returned field id lets the error
// summary move focus to the exact input that needs correcting.
export const validateStartDate = (status: string, startMonth: string, startYear: string): StartDateValidationError | null => {
  if (status !== 'active') {
    return null;
  }
  if (!startMonth && !startYear) {
    return { field: 'activity-start-month', message: 'Enter a start date, for example, 3 2026' };
  }
  if (!/^(?:0?[1-9]|1[0-2])$/.test(startMonth)) {
    return { field: 'activity-start-month', message: 'Enter a month from 1 to 12' };
  }
  if (!/^[1-9]\d{3}$/.test(startYear)) {
    return { field: 'activity-start-year', message: 'Enter a year using 4 digits' };
  }
  const enteredMonth = Number(startMonth);
  const enteredYear = Number(startYear);
  // Mirror the backend's static year bounds (InclusiveBetween 2000-2100) so a mistyped year like 1025 is
  // caught here rather than only failing server-side.
  if (enteredYear < 2000 || enteredYear > 2100) {
    return { field: 'activity-start-year', message: 'Enter a year between 2000 and 2100' };
  }
  // Reject start dates in the future. The field is month + year only, so a date in the current month is
  // allowed — only strictly-later months/years are rejected. Uses UTC to match the backend (DateTime.UtcNow)
  // so the two agree on the current month around midnight UTC.
  const now = new Date();
  if (enteredYear > now.getUTCFullYear() || (enteredYear === now.getUTCFullYear() && enteredMonth > now.getUTCMonth() + 1)) {
    return { field: 'activity-start-month', message: 'Start date must be this month or in the past' };
  }
  return null;
};

// The activity name and the custom "Other" name are optional, but the backend caps each at 255
// (Create/UpdateActivityCommandValidator's MaximumLength, keeping them inside their varchar(255) columns) — on
// drafts too, since the rule sits outside the SaveAsDraft block. Trimmed to match what buildPayload sends.
// Returns an error message or null. Shared by the create wizard and the per-section editor.
const NAME_MAX_LENGTH = 255;

export const validateNameLength = (value: string, label: string): string | null =>
  value.trim().length > NAME_MAX_LENGTH ? `${label} must be ${NAME_MAX_LENGTH} characters or less` : null;
