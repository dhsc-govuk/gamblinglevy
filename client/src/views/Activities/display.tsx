import React from 'react';
import type { ActivityStatus, ActivityTaxonomyDto, ExpectedResultDto, TaxonomyKeyResultDto } from './queries';
import type { ExpectedResultInput } from './mutations';
import type { ActivityFormState } from './reducer';

// Shared activity presentation/copy helpers — single source of truth for the bits that were previously
// duplicated across the wizard, editor, check-answers, status/type steps and the view page.
//
// The "Download activity record" CSV restates the view page server-side, so ActivityRecordPresenter.cs ports the
// section applicability, copy and status/start-date/closure-reason rules below (BLUM-7648) — change them there too.

export const PEOPLE_AND_ROLES = 'People and roles';

export const NEEDS_ASSESSMENT = 'Needs assessment';

export const EDUCATION_CAMPAIGNS_AND_OUTREACH = 'Education, campaigns and outreach';

export const HELP_ADVICE_AND_ACCESS = 'Help, advice and access to services';

// Mirrors Domain.Categories.CategoryNames.JointPartnership (BLUM-7860). Behaviour keys on the name because
// category ids are minted per environment.
export const JOINT_PARTNERSHIP = 'Joint partnership';

export const ADDITIONAL_INFORMATION_SECTION = 'Additional information';

export const isPeopleCategory = (categoryName: string | null | undefined): boolean => categoryName === PEOPLE_AND_ROLES;

export const isJointPartnershipCategory = (categoryName: string | null | undefined): boolean => categoryName === JOINT_PARTNERSHIP;

export const isNeedsAssessmentCategory = (categoryName: string | null | undefined): boolean => categoryName === NEEDS_ASSESSMENT;

export const isPeopleAndIssuesCategory = (categoryName: string | null | undefined): boolean =>
  categoryName === EDUCATION_CAMPAIGNS_AND_OUTREACH || categoryName === HELP_ADVICE_AND_ACCESS;

const DETAIL_SECTION_TITLES = ['People and issues', 'Role details', 'Activity details'];

// Which of the category-dependent detail screens belong in an activity flow: People and roles gets Role
// details alone; every other category gets the shared Activity details screen (protocol 1.BCDEF.1.1-1.3,
// BLUM-7120; 1.4 was withdrawn by BLUM-8340), and Education/Help additionally get People and issues — on every
// one of their types, whatever the answer to "What type of work does it involve?" (BLUM-7778).
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

// Which categories collect "expected results". Two do not, for the same underlying reason — no key-result
// definitions exist for any of their types — but they are listed separately rather than derived, because the
// measures screen is a product decision per category and the backend mirrors this list one exemption at a time
// (ExpectedResultInputs.ValidateCompletionAsync). Joint partnership commits money to a pooled pot, so it has no
// measures step; in the report's Results section it answers only the delivery questions (BLUM-8090).
export const categoryUsesExpectedResults = (categoryName: string | null | undefined): boolean =>
  !!categoryName && !isPeopleCategory(categoryName) && !isJointPartnershipCategory(categoryName);

// The lifecycle status a category fixes rather than asking for, or null when the user chooses it. Joint
// partnership records are Active on save (BLUM-7860 AC4): a pooled commitment has by definition already been
// made. A fixed status has no status step at all — and so no start date, which the backend stops requiring for
// this category (BLUM-8090) — and Check your answers and the view show it read-only, with no Change link.
export const forcedActivityStatus = (categoryName: string | null | undefined): string | null => (isJointPartnershipCategory(categoryName) ? 'active' : null);

// The start date the create/draft wizard submits. None at all for a fixed status: no step shows or edits one, yet a
// draft saved on the old Joint partnership "Start date" step can still hold whatever was typed there — drafts skip
// validation, so a future or impossible date sits in form state. Sending it on "Save activity" hits the backend's
// range and not-in-future rules (which still apply to any date sent), and the 400 names a field no step renders, so
// the user could neither see nor fix it. Nulling it also clears the stale value from the draft on its next save.
// The editor deliberately does not use this: a completed record's stored date was valid when it was completed.
export const submittedStartDate = (forcedStatus: string | null, startMonth: string, startYear: string): { startMonth: number | null; startYear: number | null } =>
  forcedStatus ? { startMonth: null, startYear: null } : { startMonth: startMonth ? Number(startMonth) : null, startYear: startYear ? Number(startYear) : null };

// Categories whose detail questions the create wizard asks one per page rather than one section per page. Joint
// partnership's single "Activity details" section holds two questions the Figma puts on two pages under the same
// heading, while Check your answers and the view keep them in one card with one Change link (BLUM-8090) — which is
// why this is a paging rule and not two seeded sections. The editor for a completed record still shows the whole
// section on one page: it edits a section, not a journey.
export const asksOneQuestionPerPage = (categoryName: string | null | undefined): boolean => isJointPartnershipCategory(categoryName);

// A type named "Other ..." reveals a free-text name (stored as the activity's custom task text).
export const isOtherType = (name: string): boolean => name.trim().toLowerCase().startsWith('other');

// Whether a category's record has an activity name at all. People and roles do not — roles are named by their
// type — and nor does Joint partnership (BLUM-8090). True while the category is still unknown (the taxonomy is
// loading), so a draft's name is never dropped in that window. Mirrored by ActivityRecordPresenter.ShowsNameField and
// the backend's name rule (ExpectedResultInputs.ValidateCompletionAsync).
export const categoryCollectsName = (categoryName: string | null | undefined): boolean => !isPeopleCategory(categoryName) && !isJointPartnershipCategory(categoryName);

// The activity's name is collected on the "Activity details" section of a category that has one. The wizard and the
// editor render the field behind this gate and validate it behind the same one, so the two can't drift apart; the
// wizard's payload reads categoryCollectsName directly, so it never submits a name no screen shows.
export const sectionShowsNameField = (sectionTitle: string | null | undefined, categoryName: string | null | undefined): boolean =>
  sectionTitle === 'Activity details' && categoryCollectsName(categoryName);

// Backend messages for too few key-result selections (and the messages we mirror client-side). Which one applies
// is the screen the user is looking at: a checkbox list of standard measures says "Select", the free-text-only
// "Other type of ..." screens say "Enter" (Question Protocol 13 August — BLUM-7620). Both must stay byte-identical
// to ExpectedResultInputs.SelectionError / EntryError.
export const KEY_RESULTS_SELECT_MESSAGE = 'Select at least one measure';

// Since BLUM-7792 no client-side check produces the ENTER variant — the free-text screens return before reaching
// keyResultsMinMessage. It is NOT dead: it is how messages.ts recognises the API's EntryError and puts it on the
// field instead of the generic banner, so deleting it as unused breaks that routing silently.
export const KEY_RESULTS_ENTER_MESSAGE = 'Enter at least one measure';

export const keyResultsMinMessage = (hasStandardOptions: boolean): string => (hasStandardOptions ? KEY_RESULTS_SELECT_MESSAGE : KEY_RESULTS_ENTER_MESSAGE);

// The category screen's question — shared by the category step, check-answers and the view page.
export const CATEGORY_QUESTION = 'Which category best describes this activity?';

// The activity name became mandatory in the 13 August protocol (1.BCDEF.1.1). Mirrors
// ExpectedResultInputs.NameRequiredError on the backend.
export const ACTIVITY_NAME_QUESTION = 'What is the name of this activity?';

export const ACTIVITY_NAME_REQUIRED_MESSAGE = 'Enter the name of the activity';

// Protocol hint for 1.BCDEF.1.1. It replaced "If you do not enter a name, we'll use the type of work you
// selected", which the same protocol update contradicted by making the field mandatory.
export const ACTIVITY_NAME_HINT = 'The name will appear on your quarterly report';

// Minimum key-result selections. The 13 August protocol struck needs assessment's "<2 selected" trigger through
// in favour of "<1 selected" and reworded every screen's error and hint to "at least one" — including the
// free-text-only screens, which drop from 2 entries to 1. Mirrors ExpectedResultInputs.MinimumSelections.
// BLUM-7792 then took the free-text-only screens out of this rule entirely: they require Measure 1 by position
// rather than any one row, so this count now governs the checkbox screens alone.
export const MIN_KEY_RESULT_SELECTIONS = 1;

// The expected-results question and its two hints — shared by the wizard/editor step, check-answers and the view
// page. The hint verb tracks the question's error message: checkbox screens select, free-text screens enter. The
// free-text screen's hint also has to say which of its rows is which: BLUM-7792 made Measure 1 mandatory, so "at
// least one" alone would tell a user that filling Measure 2 is enough and then refuse them for doing it. It names
// the required row rather than the optional one because the repeater renders Math.max(2, savedRows) inputs, so
// "the second is optional" would go stale on a record holding three. The label itself carries no "(optional)"
// marker because two surfaces derive SummaryList row testids from it (check-answers and the read-only view).
export const EXPECTED_RESULTS_QUESTION = 'How have you measured the progress of this activity, or how will you measure it?';

export const expectedResultsHint = (hasStandardOptions: boolean): string =>
  hasStandardOptions ? 'Select at least one measure, indicator or type of evidence' : 'Enter at least one measure, indicator or type of evidence. Only Measure 1 is required.';

// The "Other" free-text row on a checkbox screen, and the message when it is left blank.
export const OTHER_MEASURE_LABEL = 'What is the other measure you have used or will use?';

export const OTHER_MEASURE_REQUIRED_MESSAGE = 'Enter the measure you will use';

// The free-text row labels on the measures screen. Shared because three surfaces restate them — the step itself,
// Check your answers and the read-only view — and a summary that restates the question with different wording
// than the one the user answered is the drift BLUM-7620 was raised to remove.
export const measureRowLabel = (index: number): string => `Measure ${index + 1}`;

// The first free-text measure input, on either screen — "Measure 1" on the free-text screen, the "Other" reveal on
// the checkbox one. Errors about it are keyed on this id rather than on the screen-level EXPECTED_RESULTS_ERROR_KEY
// so the input gets the GDS red border and the error-summary link lands on the field itself: the error key IS the
// id, which is what lets the wizard's and the editor's `key.toLowerCase()` summary fallback resolve it unaided.
// QA note: this moved the free-text screen's inline error testid from `activities-create-expected-results-error`
// (the screen-level <p>, which now only ever renders an API refusal) to `activities-create-expected-result-0-error`.
// The error-summary link testid is unchanged, because expectedResultsErrorTargetId already pointed here.
export const FIRST_MEASURE_INPUT_ID = 'activity-expected-result-0';

// Measure 1 is mandatory on the free-text-only measures screen; Measure 2 stays optional (BLUM-7792).
export const MEASURE_ONE_REQUIRED_MESSAGE = 'Enter measure 1';

// The expected-results screen has no element carrying the `activity-expected-results` error key as its id, so an
// error-summary link built from that key alone reaches nothing. Point it at the first real input instead, the way
// questionErrorTargetId does for dynamic radio/checkbox groups: the checkbox screen ids each option
// `activity-expected-results-{code}`, and the free-text screen starts at `activity-expected-result-0`.
export const EXPECTED_RESULTS_ERROR_KEY = 'activity-expected-results';

export const expectedResultsErrorTargetId = (firstStandardCode?: string): string =>
  firstStandardCode ? `${EXPECTED_RESULTS_ERROR_KEY}-${firstStandardCode}` : FIRST_MEASURE_INPUT_ID;

// The same problem for the structural radio groups on the "Activity type" step: their error keys
// (`activity-category` / `activity-type`) name no element, because GovUKFieldset.RadioGroup ids each input
// `{name}-{optionValue}`. GDS requires an error-summary link to reach a real field, so point it at the group's
// first option — the fallback to the bare name only applies before the taxonomy has loaded, when no radio exists
// to link to anyway.
export const radioGroupErrorTargetId = (name: string, firstOptionValue?: string): string => (firstOptionValue ? `${name}-${firstOptionValue}` : name);

// Option-level hints on the category radios, keyed by the seeded category name. Same shape and purpose as
// TYPE_OPTION_HINTS below, one level up.
export const CATEGORY_OPTION_HINTS: Record<string, string> = {
  [JOINT_PARTNERSHIP]: 'Levy funding you have pooled with other organisations into a shared pot',
};

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
  'Other type of help, advice or service': 'Other type of help, advice or access to a service',
};

// The success banner shown after an activity is saved. Lives here rather than in the list that usually renders
// it because ActivityWizard now raises the same copy as a GovUKNotification when a save happens on the way out
// of an intercepted navigation and the user lands somewhere else entirely (BLUM-7831) — two renderings of one
// confirmation, and they must not drift.
export type ActivityBannerKey = 'created' | 'updated' | 'draft' | 'discarded' | 'deleted';

export const ACTIVITY_BANNER_COPY: Record<ActivityBannerKey, { title: string; body: (name: string) => string }> = {
  created: { title: 'Activity created', body: (name) => `‘${name}’ is now on your list of activities.` },
  updated: { title: 'Activity updated', body: (name) => `Your changes to ‘${name}’ are now saved.` },
  draft: { title: 'Progress saved', body: (name) => `You can come back to the activity ‘${name}’ and continue later.` },
  discarded: { title: 'Changes discarded', body: (name) => `The updates you made to the activity ‘${name}’ were not saved.` },
  deleted: { title: 'Activity deleted', body: (name) => `‘${name}’ has been taken off your main list. You can restore it from your deleted activities.` },
};

export const activityTypeDisplayName = (name: string): string => TYPE_DISPLAY_NAMES[name] ?? name;

// The standard protocol key results offered for an activity type, in seed order.
export const keyResultsForTask = (taxonomy: ActivityTaxonomyDto | undefined, taskId: string): TaxonomyKeyResultDto[] =>
  (taxonomy?.keyResults ?? []).filter((keyResult) => keyResult.taskId === taskId);

// The two measures screens have two different rules. A free-text-only screen requires its FIRST row by position;
// a checkbox screen requires enough selections for the category, counting standard checkboxes plus non-empty
// custom "Other" texts (and its stale codes are moot, since the free-text path returns before reading them).
export const validateKeyResultSelections = (
  state: Pick<ActivityFormState, 'keyResultCodes' | 'expectedResults' | 'otherExpectedResultSelected'>,
  hasStandardOptions: boolean
): Record<string, string> => {
  // Free-text-only screens ("Other type of ..."): Measure 1 is mandatory and Measure 2 optional (BLUM-7792). A
  // filled Measure 2 used to satisfy the screen on its own, because the rule was an aggregate count over the whole
  // repeater. It cannot be enforced server-side: blank rows are stripped from the payload, so the API sees the same
  // one-row list either way and its own minimum of 1 already passes. That stripping is also why the rule can never
  // lock an existing record out — a draft saved as (blank, "footfall") reloads as ("footfall", blank), so the text
  // comes back under the Measure 1 label rather than failing validation on reopen.
  if (!hasStandardOptions) {
    return (state.expectedResults[0] ?? '').trim() ? {} : { [FIRST_MEASURE_INPUT_ID]: MEASURE_ONE_REQUIRED_MESSAGE };
  }
  const customCount = state.expectedResults.filter((text) => text.trim()).length;
  if (state.otherExpectedResultSelected && customCount === 0) {
    return { [FIRST_MEASURE_INPUT_ID]: OTHER_MEASURE_REQUIRED_MESSAGE };
  }
  const selected = state.keyResultCodes.length + customCount;
  return selected >= MIN_KEY_RESULT_SELECTIONS ? {} : { [EXPECTED_RESULTS_ERROR_KEY]: keyResultsMinMessage(hasStandardOptions) };
};

// Splits an ActivityDto's expected-result rows into form state: standard selections (codes) and custom
// free-text rows (at least one empty input so the "Other" repeater renders).
export const splitExpectedResults = (rows: ExpectedResultDto[]): { keyResultCodes: string[]; expectedResults: string[]; otherExpectedResultSelected: boolean } => {
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
  // The free text the Closed option reveals (BLUM-7673).
  closureQuestion: string;
  closureHint: string;
  closureError: string;
}

// Role-vs-activity wording for the type/status screens, keyed off the People-and-roles category. The category name
// is still accepted from every caller, but nothing overrides per category any more: Joint partnership's "Start date"
// wording went with its start-date step (BLUM-8090), and its read-only status row uses the activity wording as is.
export const activityCopy = (isPeople: boolean, _categoryName?: string | null): ActivityCopy => baseActivityCopy(isPeople);

const baseActivityCopy = (isPeople: boolean): ActivityCopy =>
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
        // Sheet 1.A.5 (13 August): the role screen's status error is worded "Select the activity status", the
        // same as the activity screen's — the protocol treats a role as one kind of activity record.
        statusError: 'Select the activity status',
        startQuestion: 'When did the staff member start work?',
        statusHints: {
          planned: 'Empty and still needed',
          active: 'Filled and ongoing',
          closed: 'Ended and no longer needed',
          finished: 'All work is complete and the position is empty',
        },
        // Not in the Figma, which only shows the activity screen (BLUM-7673): role wording to match the rest of this
        // screen, with the activity screen's hint.
        closureQuestion: 'Why is this role being closed?',
        closureHint: 'For example, no longer needed',
        closureError: 'Enter why the role is being closed',
      }
    : {
        typeTitle: 'Activity type',
        typeQuestion: 'What type of work does it involve?',
        typeHint: 'Create a new activity for each type',
        typeError: 'Select the type of work involved',
        otherLabel: "What's the activity?",
        otherError: 'Enter the activity',
        otherNameLabel: 'Activity',
        statusTitle: 'Activity status',
        statusQuestion: 'What’s the status of this activity?',
        statusError: 'Select the activity status',
        startQuestion: 'When did the work start?',
        statusHints: {
          planned: 'Approved but work has not started',
          active: 'Work is underway',
          closed: 'Stopped before completion',
          finished: 'All work is complete',
        },
        // Figma 3201:339943 (BLUM-7673). The error is ours — the design gives none.
        closureQuestion: 'Why is this activity being closed?',
        closureHint: 'For example, no longer needed',
        closureError: 'Enter why the activity is being closed',
      };

interface StartDateValidationError {
  field: 'activity-start-month' | 'activity-start-year';
  message: string;
}

export const START_DATE_MESSAGES = {
  missing: 'Enter a start date',
  missingMonth: 'The start date must include a month',
  missingYear: 'The start date must include a year',
  yearDigits: 'Year must include 4 numbers',
  notReal: 'The start date must be a real date',
  inFuture: 'The start date must be today or in the past',
} as const;

// Active activities require a start month (1-12) and four-digit year. The returned field id lets the error
// summary move focus to the exact input that needs correcting. One message per error trigger, worded exactly as
// the 13 August protocol lists them for 1.A.6 and 1.BCDEF.1.5-other (BLUM-7620); the backend's
// Create/UpdateActivityCommandValidator mirrors them.
export const validateStartDate = (status: string, startMonth: string, startYear: string): StartDateValidationError | null => {
  if (status !== 'active') {
    return null;
  }
  if (!startMonth && !startYear) {
    return { field: 'activity-start-month', message: START_DATE_MESSAGES.missing };
  }
  if (!startMonth) {
    return { field: 'activity-start-month', message: START_DATE_MESSAGES.missingMonth };
  }
  if (!startYear) {
    return { field: 'activity-start-year', message: START_DATE_MESSAGES.missingYear };
  }
  if (!/^\d{4}$/.test(startYear)) {
    return { field: 'activity-start-year', message: START_DATE_MESSAGES.yearDigits };
  }
  // "A real date" covers both an impossible month (the protocol's own example, 13) and a year outside the
  // backend's static bounds (InclusiveBetween 2000-2100), so a mistyped 1025 is caught here rather than only
  // failing server-side. One message covers both parts, so — per GDS's rule for a whole-date error — it links to
  // the first field of the date input rather than to whichever half is wrong.
  const enteredMonth = Number(startMonth);
  const enteredYear = Number(startYear);
  if (!/^(?:0?[1-9]|1[0-2])$/.test(startMonth) || enteredYear < 2000 || enteredYear > 2100) {
    return { field: 'activity-start-month', message: START_DATE_MESSAGES.notReal };
  }
  // Reject start dates in the future. The field is month + year only, so a date in the current month is
  // allowed — only strictly-later months/years are rejected. Uses UTC to match the backend (DateTime.UtcNow)
  // so the two agree on the current month around midnight UTC.
  const now = new Date();
  if (enteredYear > now.getUTCFullYear() || (enteredYear === now.getUTCFullYear() && enteredMonth > now.getUTCMonth() + 1)) {
    return { field: 'activity-start-month', message: START_DATE_MESSAGES.inFuture };
  }
  return null;
};

// Which half of the start date to outline red for the error on show. An error is keyed to the input its summary link
// focuses, which for a whole-date error is the month (the first field); so only the missing-month message is about
// the month alone. Undefined means the whole date: GovUKDateField then outlines both inputs.
export const startDateErrorParts = (errors: Record<string, string | undefined>): ('month' | 'year')[] | undefined => {
  if (errors['activity-start-year']) {
    return ['year'];
  }
  if (errors['activity-start-month'] === START_DATE_MESSAGES.missingMonth) {
    return ['month'];
  }
  return undefined;
};

// Why a Closed activity was closed (BLUM-7673): required when completing, optional on a draft — the same split as the
// activity name — and capped at 500 either way, because the backend's varchar(500) rule sits outside its SaveAsDraft
// block (Create/UpdateActivityCommandValidator, whose messages match these). Trimmed to match what is submitted.
const CLOSURE_REASON_MAX_LENGTH = 500;

export const validateClosureReason = (status: string, value: string, enforceRequired: boolean, copy: ActivityCopy): string | null => {
  if (status !== 'closed') {
    return null;
  }
  const trimmed = value.trim();
  if (!trimmed) {
    return enforceRequired ? copy.closureError : null;
  }
  return trimmed.length > CLOSURE_REASON_MAX_LENGTH ? `Reason for closure must be ${CLOSURE_REASON_MAX_LENGTH} characters or less` : null;
};

// Only a Closed activity keeps a reason; the backend drops it for any other status as well.
export const submittedClosureReason = (status: string, value: string): string | null => (status === 'closed' ? value.trim() || null : null);

// The custom "Other" name is optional and the activity name is required on completion (see
// validateActivityName below); the backend caps each at 255
// (Create/UpdateActivityCommandValidator's MaximumLength, keeping them inside their varchar(255) columns) — on
// drafts too, since the rule sits outside the SaveAsDraft block. Trimmed to match what buildPayload sends.
// Returns an error message or null. Shared by the create wizard and the per-section editor.
const NAME_MAX_LENGTH = 255;

export const validateNameLength = (value: string, label: string): string | null =>
  value.trim().length > NAME_MAX_LENGTH ? `${label} must be ${NAME_MAX_LENGTH} characters or less` : null;

// The activity name on the "Activity details" step: mandatory since the 13 August protocol, but only when
// completing — a draft may still be saved without one, matching the backend, which runs the rule inside its
// `!SaveAsDraft` block. The length rule applies either way, because the column is varchar(255) on drafts too.
export const validateActivityName = (value: string, enforceRequired: boolean): string | null => {
  if (!value.trim()) {
    return enforceRequired ? ACTIVITY_NAME_REQUIRED_MESSAGE : null;
  }
  return validateNameLength(value, 'Activity name');
};
