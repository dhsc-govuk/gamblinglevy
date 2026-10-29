import type { CreateCollectionInput, UpdateCollectionInput } from './mutations';
import type { CollectionDto, CollectionFormOptionDto } from './queries';

// Form state for the add-collection wizard. Backend enum string values are used verbatim for
// `type` and reminder `offsetType` so `buildCreateInput` is a thin mapping with no extra lookup tables.

export type CollectionFormType = '' | 'quarterly_report' | 'maturity_assessment';
export type YesNo = '' | 'yes' | 'no';
export type ReminderOffset = '' | 'after_open' | 'before_deadline';

// No shared bridge exists between the backend's FieldLimits and the client, so these mirror
// Application.Common.Validation.FieldLimits manually — keep them in sync by hand if either changes.
export const COLLECTION_PURPOSE_MAX_LENGTH = 2000; // FieldLimits.Purpose
export const COLLECTION_MAX_REMINDER_DAYS = 730; // FieldLimits.MaxReminderDays
export const COLLECTION_DATE_MIN_YEAR = 2000; // FieldLimits.MinDateYear
export const COLLECTION_DATE_MAX_YEAR = 2100; // FieldLimits.MaxDateYear

export interface CollectionFormReminder {
  id: string;
  offsetType: ReminderOffset;
  days: string;
}

export interface CollectionFormState {
  name: string;
  purpose: string;
  type: CollectionFormType;
  financialYear: string;
  quarter: string; // '' or '1'..'4'
  windowSameAsReportingPeriod: YesNo;
  opensDay: string;
  opensMonth: string;
  opensYear: string;
  deadlineDay: string;
  deadlineMonth: string;
  deadlineYear: string;
  formModuleIds: string[];
  // BLUM-9103: the version chosen per form id — '' for "latest version", else the version number as text.
  formVersions: Record<string, string>;
  // BLUM-9103: the version each form records on a saved collection, shown as a hint beside the choice. Read-only.
  recordedFormVersions: Record<string, number>;
  // Whether that recorded version was DHSC's choice. "Latest version" keeps a version recorded at publish, but clears a
  // chosen one, which the save replaces with the newest — so check answers must not show the recorded number then.
  recordedVersionChosen: Record<string, boolean>;
  // The type whose default form selection (every form ticked) has already been applied — see APPLY_DEFAULT_FORMS.
  // Kept in reducer state rather than a ref so it survives the discard round-trip, which restores the whole state.
  formsDefaultAppliedFor: CollectionFormType;
  isSubmittedByAllLas: YesNo;
  selectedLaIds: string[];
  reminders: CollectionFormReminder[];
  errors: Record<string, string>;
  submitAttempts: number;
}

export type CollectionFormAction =
  | { type: 'SET_FIELD'; field: keyof CollectionFormState; value: string }
  | { type: 'SET_TYPE'; value: CollectionFormType }
  | { type: 'SET_WINDOW_SAME'; value: YesNo }
  | { type: 'TOGGLE_FORM'; id: string }
  | { type: 'SET_FORM_VERSION'; id: string; value: string }
  | { type: 'APPLY_DEFAULT_FORMS'; forType: CollectionFormType; availableIds: string[] }
  | { type: 'SET_ALL_LAS'; value: YesNo }
  | { type: 'TOGGLE_LA'; id: string }
  | { type: 'ADD_LAS'; ids: string[] }
  | { type: 'REMOVE_LAS'; ids: string[] }
  | { type: 'ENSURE_REMINDER' }
  | { type: 'ADD_REMINDER' }
  | { type: 'REMOVE_REMINDER'; id: string }
  | { type: 'SET_REMINDER'; id: string; patch: Partial<Omit<CollectionFormReminder, 'id'>> }
  | { type: 'SET_ERRORS'; errors: Record<string, string> }
  | { type: 'BUMP_ATTEMPTS' }
  | { type: 'HYDRATE'; state: Partial<CollectionFormState> };

export const initialCollectionFormState: CollectionFormState = {
  name: '',
  purpose: '',
  type: '',
  financialYear: '',
  quarter: '',
  windowSameAsReportingPeriod: '',
  opensDay: '',
  opensMonth: '',
  opensYear: '',
  deadlineDay: '',
  deadlineMonth: '',
  deadlineYear: '',
  formModuleIds: [],
  formVersions: {},
  recordedFormVersions: {},
  recordedVersionChosen: {},
  formsDefaultAppliedFor: '',
  isSubmittedByAllLas: '',
  selectedLaIds: [],
  reminders: [],
  errors: {},
  submitAttempts: 0,
};

const toggle = (list: string[], id: string): string[] => (list.includes(id) ? list.filter((value) => value !== id) : [...list, id]);

const blankReminder = (): CollectionFormReminder => ({ id: crypto.randomUUID(), offsetType: '', days: '' });

export const collectionFormReducer = (state: CollectionFormState, action: CollectionFormAction): CollectionFormState => {
  switch (action.type) {
    case 'SET_FIELD':
      return { ...state, [action.field]: action.value };
    case 'SET_TYPE': {
      const errors = { ...state.errors };
      // Quarter only applies to quarterly reports — clear it for any other type.
      if (action.value !== 'quarterly_report') {
        delete errors['quarter'];
      }
      // Forms belong to a type (BLUM-7943), so a switch drops the other type's selection and the new type's
      // default is applied once its options are known.
      const typeChanged = action.value !== state.type;
      if (typeChanged) {
        delete errors['forms'];
      }
      return {
        ...state,
        type: action.value,
        quarter: action.value === 'quarterly_report' ? state.quarter : '',
        formModuleIds: typeChanged ? [] : state.formModuleIds,
        formVersions: typeChanged ? {} : state.formVersions,
        recordedFormVersions: typeChanged ? {} : state.recordedFormVersions,
        recordedVersionChosen: typeChanged ? {} : state.recordedVersionChosen,
        errors,
      };
    }
    case 'SET_WINDOW_SAME':
      // When the window mirrors the reporting period the explicit open/deadline dates are unused.
      if (action.value === 'yes') {
        const errors = { ...state.errors };
        delete errors['window-same'];
        delete errors['open-date'];
        delete errors['open-date-day'];
        delete errors['open-date-month'];
        delete errors['open-date-year'];
        delete errors['open-date-invalid'];
        delete errors['open-date-future'];
        delete errors['deadline-date'];
        delete errors['deadline-date-day'];
        delete errors['deadline-date-month'];
        delete errors['deadline-date-year'];
        delete errors['deadline-date-invalid'];
        delete errors['deadline-date-future'];
        delete errors['deadline-date-order'];
        return {
          ...state,
          windowSameAsReportingPeriod: action.value,
          opensDay: '',
          opensMonth: '',
          opensYear: '',
          deadlineDay: '',
          deadlineMonth: '',
          deadlineYear: '',
          errors,
        };
      }
      if (action.value === 'no') {
        const errors = { ...state.errors };
        delete errors['window-same'];
        return { ...state, windowSameAsReportingPeriod: action.value, errors };
      }
      return { ...state, windowSameAsReportingPeriod: action.value };
    case 'TOGGLE_FORM':
      return { ...state, formModuleIds: toggle(state.formModuleIds, action.id) };
    case 'SET_FORM_VERSION':
      return { ...state, formVersions: { ...state.formVersions, [action.id]: action.value } };
    case 'APPLY_DEFAULT_FORMS': {
      // Every form of the type starts ticked (BLUM-7943), applied once per type: after that an empty selection is
      // the admin's own choice and gets the "Select at least one form" error, not a silent re-tick.
      //
      // A saved selection (resumed draft, edited collection) is kept exactly as stored — never trimmed to the
      // current options. Those only list ACTIVE modules, so trimming would silently drop a since-deactivated module
      // the moment an admin saved an unrelated edit, and record it as their change. A type switch has already
      // emptied the selection (SET_TYPE), and legacy maturity collections were re-pointed by the BLUM-7943 migration.
      if (action.forType !== state.type || state.formsDefaultAppliedFor === action.forType) {
        return state;
      }
      const formModuleIds = state.formModuleIds.length > 0 ? state.formModuleIds : action.availableIds;
      const errors = { ...state.errors };
      if (formModuleIds.length > 0) {
        delete errors['forms'];
      }
      return { ...state, formModuleIds, formsDefaultAppliedFor: action.forType, errors };
    }
    case 'SET_ALL_LAS':
      // "All LAs" makes the explicit selection redundant — clear it.
      if (action.value === 'yes') {
        const errors = { ...state.errors };
        delete errors['all-las'];
        delete errors['selected-las'];
        return { ...state, isSubmittedByAllLas: action.value, selectedLaIds: [], errors };
      }
      if (action.value === 'no') {
        const errors = { ...state.errors };
        delete errors['all-las'];
        return { ...state, isSubmittedByAllLas: action.value, errors };
      }
      return { ...state, isSubmittedByAllLas: action.value };
    case 'TOGGLE_LA': {
      const selectedLaIds = toggle(state.selectedLaIds, action.id);
      const errors = { ...state.errors };
      if (selectedLaIds.length > 0) {
        delete errors['selected-las'];
      }
      return { ...state, selectedLaIds, errors };
    }
    case 'ADD_LAS': {
      // Union — used by the selector's "All" toggle to add only the LAs currently matching the search.
      const selectedLaIds = Array.from(new Set([...state.selectedLaIds, ...action.ids]));
      const errors = { ...state.errors };
      if (selectedLaIds.length > 0) {
        delete errors['selected-las'];
      }
      return { ...state, selectedLaIds, errors };
    }
    case 'REMOVE_LAS':
      // Difference — clears only the LAs currently matching the search, leaving other selections intact.
      return { ...state, selectedLaIds: state.selectedLaIds.filter((id) => !action.ids.includes(id)) };
    case 'ENSURE_REMINDER':
      return state.reminders.length > 0 ? state : { ...state, reminders: [blankReminder()] };
    case 'ADD_REMINDER':
      return {
        ...state,
        reminders: [...state.reminders, blankReminder()],
      };
    case 'REMOVE_REMINDER': {
      const reminders = state.reminders.filter((reminder) => reminder.id !== action.id);
      // Reminder errors are keyed by index, so removing one shifts every later block up and would
      // leave each stale error sitting under the wrong reminder — and each message now names the
      // reminder it belongs to, so a stale one visibly contradicts its heading. Clearing is enough:
      // the step re-validates on Continue.
      const errors = Object.fromEntries(Object.entries(state.errors).filter(([key]) => !key.startsWith('reminder-')));
      return { ...state, reminders: reminders.length > 0 ? reminders : [blankReminder()], errors };
    }
    case 'SET_REMINDER': {
      const reminderIndex = state.reminders.findIndex((reminder) => reminder.id === action.id);
      const errors = { ...state.errors };
      if (reminderIndex >= 0 && action.patch.days !== undefined) {
        const days = action.patch.days.trim();
        if (/^\d+$/.test(days) && Number(days) <= COLLECTION_MAX_REMINDER_DAYS) {
          delete errors[`reminder-${reminderIndex}-days`];
        }
      }
      if (reminderIndex >= 0 && action.patch.offsetType) {
        delete errors[`reminder-${reminderIndex}-trigger`];
        // The days message is direction-aware ("after the collection opens" vs "before the submission
        // deadline") — switching direction while a days error is showing would otherwise leave the old
        // direction's wording attached to the newly-selected field until the next Continue.
        delete errors[`reminder-${reminderIndex}-days`];
      }
      return {
        ...state,
        reminders: state.reminders.map((reminder) => (reminder.id === action.id ? { ...reminder, ...action.patch } : reminder)),
        errors,
      };
    }
    case 'SET_ERRORS':
      return { ...state, errors: action.errors };
    case 'BUMP_ATTEMPTS':
      return { ...state, submitAttempts: state.submitAttempts + 1 };
    case 'HYDRATE':
      return { ...state, ...action.state };
    default:
      return state;
  }
};

// Granular so each missing/invalid part of a GOV.UK date input gets its own message rather than one
// generic "enter a valid date" — 'invalid' means all three parts are present and in range but don't
// combine into a real calendar date (e.g. 31 February).
export type DatePartValidationIssue = 'missing-all' | 'missing-day' | 'missing-month' | 'missing-year' | 'day-range' | 'month-range' | 'year-format' | 'year-range' | 'invalid';

// All three day/month/year parts present as plain integers, within the allowed ranges, and combinable
// into a valid calendar date.
export const datePartsValidationIssue = (day: string, month: string, year: string): DatePartValidationIssue | null => {
  const dayEmpty = !day.trim();
  const monthEmpty = !month.trim();
  const yearEmpty = !year.trim();
  if (dayEmpty && monthEmpty && yearEmpty) {
    return 'missing-all';
  }
  if (dayEmpty) {
    return 'missing-day';
  }
  if (monthEmpty) {
    return 'missing-month';
  }
  if (yearEmpty) {
    return 'missing-year';
  }
  if (!/^\d{1,2}$/.test(day)) {
    return 'day-range';
  }
  if (!/^\d{1,2}$/.test(month)) {
    return 'month-range';
  }
  if (!/^\d{4}$/.test(year)) {
    return 'year-format';
  }
  const d = Number(day);
  const m = Number(month);
  const y = Number(year);
  if (d < 1 || d > 31) {
    return 'day-range';
  }
  if (m < 1 || m > 12) {
    return 'month-range';
  }
  // A four-digit year can still be nonsense (5050, BLUM-8053); the backend enforces the same range.
  if (y < COLLECTION_DATE_MIN_YEAR || y > COLLECTION_DATE_MAX_YEAR) {
    return 'year-range';
  }
  const date = new Date(Date.UTC(y, m - 1, d));
  if (date.getUTCFullYear() !== y || date.getUTCMonth() !== m - 1 || date.getUTCDate() !== d) {
    return 'invalid';
  }
  return null;
};

export const datePartsValid = (day: string, month: string, year: string): boolean => datePartsValidationIssue(day, month, year) === null;

// UTC-midnight comparison against "today" — same construction style as the calendar round-trip check
// above, so a date entered in any timezone is compared against the same notion of "today" consistently.
// >=, not >: BLUM-8253 — a collection opening today is not a mistake, only one opening before today is.
export const isDateTodayOrFuture = (day: string, month: string, year: string): boolean => {
  const value = Date.UTC(Number(year), Number(month) - 1, Number(day));
  const now = new Date();
  const today = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());
  return value >= today;
};

// Combines GOV.UK day/month/year inputs into an ISO `YYYY-MM-DD` date string (zero-padded).
export const isoFromParts = (day: string, month: string, year: string): string => `${year.padStart(4, '0')}-${month.padStart(2, '0')}-${day.padStart(2, '0')}`;

// The API binds dates as nullable DateOnly, so anything short of a complete valid date (e.g. the
// untouched window step when saving a draft early) must be omitted rather than sent malformed.
const isoFromPartsOrUndefined = (day: string, month: string, year: string): string | undefined => (datePartsValid(day, month, year) ? isoFromParts(day, month, year) : undefined);

// `''` (no quarter picked yet — allowed for drafts) must reach the API as null, not Number('') === 0.
const quarterOrNull = (state: CollectionFormState): number | null => (state.type === 'quarterly_report' && state.quarter ? Number(state.quarter) : null);

// Unanswered yes/no questions ('') reach the API as null — they must never be coerced to "No"/false,
// or a resumed draft would show answers the user never gave.
const yesNoToBoolOrNull = (value: YesNo): boolean | null => (value === '' ? null : value === 'yes');

// Maps the wizard state onto the create request. `saveAsDraft` is threaded through unchanged so the
// same builder serves both the "Save and come back later" and final "Create collection" actions.
// BLUM-9103: the version choice for every selected form of a quarterly report, sent only when the choice is offered
// (BLUM_9087); otherwise undefined, so the API keeps each form's version as it was.
const formVersionsInput = (state: CollectionFormState, includeFormVersions: boolean): { moduleId: string; versionNumber: number | null }[] | undefined =>
  includeFormVersions && state.type === 'quarterly_report'
    ? state.formModuleIds.map((moduleId) => ({ moduleId, versionNumber: state.formVersions[moduleId] ? Number(state.formVersions[moduleId]) : null }))
    : undefined;

// BLUM-9103: the version check answers says a form will use, or undefined for "the latest". A choice wins; then the
// version a published collection recorded at publish, which "Latest version" keeps (a later version will not change
// it). "Latest version" on a version DHSC had chosen clears that choice, so the save records the newest version, not
// the recorded one.
export const formVersionShown = (state: CollectionFormState, moduleId: string): string | number | undefined =>
  state.formVersions[moduleId] || (state.recordedVersionChosen[moduleId] ? undefined : state.recordedFormVersions[moduleId]);

export const buildCreateInput = (state: CollectionFormState, saveAsDraft: boolean, includeFormVersions = false): CreateCollectionInput => {
  const sameWindow = state.windowSameAsReportingPeriod === 'yes';
  const allLas = state.isSubmittedByAllLas === 'yes';
  return {
    name: state.name,
    purpose: state.purpose || undefined,
    type: state.type || undefined,
    financialYear: state.financialYear || undefined,
    quarter: quarterOrNull(state),
    windowSameAsReportingPeriod: yesNoToBoolOrNull(state.windowSameAsReportingPeriod),
    openDate: sameWindow ? undefined : isoFromPartsOrUndefined(state.opensDay, state.opensMonth, state.opensYear),
    deadline: sameWindow ? undefined : isoFromPartsOrUndefined(state.deadlineDay, state.deadlineMonth, state.deadlineYear),
    isSubmittedByAllLas: yesNoToBoolOrNull(state.isSubmittedByAllLas),
    saveAsDraft,
    localAuthorityIds: allLas ? [] : state.selectedLaIds,
    formModuleIds: state.formModuleIds,
    formVersions: formVersionsInput(state, includeFormVersions),
    reminders: state.reminders
      .filter((reminder) => reminder.offsetType)
      .map((reminder) => ({
        offsetType: reminder.offsetType as Exclude<ReminderOffset, ''>,
        days: Number(reminder.days || '0'),
      })),
  };
};

// Splits an ISO `YYYY-MM-DD` date into GOV.UK day/month/year input parts. Returns empty parts for a
// missing/blank date (e.g. when the window mirrors the reporting period and no explicit dates exist).
const partsFromIso = (iso: string | null | undefined): { day: string; month: string; year: string } => {
  if (!iso) {
    return { day: '', month: '', year: '' };
  }
  const [year = '', month = '', day = ''] = iso.split('-');
  // Strip the zero-padding the API uses so the inputs show natural values ("1" not "01").
  return {
    day: day ? String(Number(day)) : '',
    month: month ? String(Number(month)) : '',
    year: year ? String(Number(year)) : '',
  };
};

// Null (question not answered yet — drafts only) maps back to the unanswered '' state, NOT to 'no'.
const boolToYesNo = (value: boolean | null): YesNo => (value == null ? '' : value ? 'yes' : 'no');

// Maps a persisted collection back onto the wizard's form state so the edit flow can pre-populate the
// same controls used by create. When the window mirrors the reporting period the open/deadline inputs
// are intentionally left blank (the explicit dates are unused in that mode).
export function hydrateCollectionState(dto: CollectionDto): Partial<CollectionFormState> {
  const sameWindow = dto.windowSameAsReportingPeriod === true;
  const opens = partsFromIso(sameWindow ? '' : dto.opensDate);
  const deadline = partsFromIso(sameWindow ? '' : dto.deadline);
  return {
    name: dto.name,
    purpose: dto.purpose ?? '',
    type: dto.type ?? '',
    financialYear: dto.financialYear ?? '',
    quarter: dto.quarter == null ? '' : String(dto.quarter),
    windowSameAsReportingPeriod: boolToYesNo(dto.windowSameAsReportingPeriod),
    opensDay: opens.day,
    opensMonth: opens.month,
    opensYear: opens.year,
    deadlineDay: deadline.day,
    deadlineMonth: deadline.month,
    deadlineYear: deadline.year,
    formModuleIds: dto.forms.map((form) => form.moduleId),
    // A chosen version shows as chosen; a form on "the latest at publish" shows as "Latest version", with the
    // version it records as a hint.
    formVersions: Object.fromEntries(dto.forms.map((form) => [form.moduleId, form.versionChosen && form.versionNumber ? String(form.versionNumber) : ''])),
    recordedFormVersions: Object.fromEntries(dto.forms.filter((form) => form.versionNumber !== null).map((form) => [form.moduleId, form.versionNumber as number])),
    recordedVersionChosen: Object.fromEntries(dto.forms.map((form) => [form.moduleId, form.versionChosen])),
    isSubmittedByAllLas: boolToYesNo(dto.isSubmittedByAllLas),
    selectedLaIds: dto.localAuthorities.map((la) => la.id),
    reminders: dto.reminders.map((reminder) => ({
      id: crypto.randomUUID(),
      offsetType: reminder.offsetType,
      days: String(reminder.days),
    })),
  };
}

// Maps the wizard state onto the update request. Identical to `buildCreateInput` except the API renames
// the date fields for updates: `collectionOpenDate` / `submissionDeadline` (vs create's openDate/deadline).
export function buildUpdateInput(state: CollectionFormState, saveAsDraft: boolean, includeFormVersions = false): UpdateCollectionInput {
  const sameWindow = state.windowSameAsReportingPeriod === 'yes';
  const allLas = state.isSubmittedByAllLas === 'yes';
  return {
    id: '',
    name: state.name,
    purpose: state.purpose || undefined,
    type: state.type || undefined,
    financialYear: state.financialYear || undefined,
    quarter: quarterOrNull(state),
    windowSameAsReportingPeriod: yesNoToBoolOrNull(state.windowSameAsReportingPeriod),
    collectionOpenDate: sameWindow ? undefined : isoFromPartsOrUndefined(state.opensDay, state.opensMonth, state.opensYear),
    submissionDeadline: sameWindow ? undefined : isoFromPartsOrUndefined(state.deadlineDay, state.deadlineMonth, state.deadlineYear),
    isSubmittedByAllLas: yesNoToBoolOrNull(state.isSubmittedByAllLas),
    saveAsDraft,
    localAuthorityIds: allLas ? [] : state.selectedLaIds,
    formModuleIds: state.formModuleIds,
    formVersions: formVersionsInput(state, includeFormVersions),
    reminders: state.reminders
      .filter((reminder) => reminder.offsetType)
      .map((reminder) => ({
        offsetType: reminder.offsetType as Exclude<ReminderOffset, ''>,
        days: Number(reminder.days || '0'),
      })),
  };
}

// The Forms step's options for the chosen type (BLUM-7943): a quarterly report's form modules or a maturity
// assessment's catalogue sections. Nothing is offered until a type is picked.
export const formOptionsForType = (options: CollectionFormOptionDto[], type: CollectionFormType): CollectionFormOptionDto[] =>
  type ? options.filter((option) => option.type === type) : [];

export type CollectionSectionKind = 'details' | 'window' | 'forms' | 'reportingLas' | 'reminders';

const dateFieldMessages = (key: 'open-date' | 'deadline-date', issue: DatePartValidationIssue): Record<string, string> => {
  const isOpen = key === 'open-date';
  switch (issue) {
    case 'missing-all':
      return { [key]: isOpen ? 'Enter the date the data collection will open' : 'Enter the date of the submission deadline' };
    case 'missing-day':
      return { [`${key}-day`]: isOpen ? 'Enter the day the data collection will open' : 'Enter the day of the submission deadline' };
    case 'missing-month':
      return { [`${key}-month`]: isOpen ? 'Enter the month the data collection will open' : 'Enter the month of the submission deadline' };
    case 'missing-year':
      return { [`${key}-year`]: isOpen ? 'Enter the year the data collection will open' : 'Enter the year of the submission deadline' };
    case 'day-range':
      return { [`${key}-day`]: isOpen ? 'Day collection opens must be a number between 1 and 31' : 'Day of submission deadline must be a number between 1 and 31' };
    case 'month-range':
      return { [`${key}-month`]: isOpen ? 'Month collection opens must be a number between 1 and 12' : 'Month of submission deadline must be a number between 1 and 12' };
    case 'year-format':
      return { [`${key}-year`]: isOpen ? 'Year collection opens must be four numbers' : 'Year of submission deadline must be four numbers' };
    case 'year-range':
      return {
        [`${key}-year`]: isOpen
          ? `Year collection opens must be between ${COLLECTION_DATE_MIN_YEAR} and ${COLLECTION_DATE_MAX_YEAR}`
          : `Year of submission deadline must be between ${COLLECTION_DATE_MIN_YEAR} and ${COLLECTION_DATE_MAX_YEAR}`,
      };
    case 'invalid':
      return { [`${key}-invalid`]: isOpen ? 'Collection open date must be a real date' : 'Submission deadline must be a real date' };
    default:
      return {};
  }
};

// Shared by CollectionWizard (create, and draft resume) and CollectionEditor (section-by-section edit
// of a scheduled/open collection) so both stay in sync. Both window dates must be today or later
// (BLUM-8161), with one exemption: `savedOpenDate`, the ISO open date CollectionEditor passes for the
// collection it is editing. An already-open collection's open date is legitimately in the past, and
// CollectionEditor re-validates every section on every save, so without the exemption its unrelated fields
// (e.g. its reminders) could never be saved — but changing that date to any other past date is refused.
// No deadline exemption is needed: a saved deadline before today means the collection is Closed, and a
// closed collection can't be edited. The wizard passes none — a draft hasn't opened yet.
export const validateCollectionSection = (section: CollectionSectionKind, state: CollectionFormState, options: { savedOpenDate?: string | null } = {}): Record<string, string> => {
  const errors: Record<string, string> = {};

  switch (section) {
    case 'details': {
      if (!state.name.trim()) {
        errors['name'] = 'Enter a name for this data collection';
      }
      if (!state.type) {
        errors['type'] = 'Select if the report will be a quarterly report or a maturity assessment';
      }
      if (state.type && !state.financialYear) {
        errors['financialYear'] = state.type === 'quarterly_report' ? 'Select the financial year of the quarterly report' : 'Select the financial year of the maturity assessment';
      }
      if (state.type === 'quarterly_report' && !state.quarter) {
        errors['quarter'] = 'Select the quarter of the quarterly report';
      }
      if (state.purpose.length > COLLECTION_PURPOSE_MAX_LENGTH) {
        errors['purpose'] = `Purpose must be ${COLLECTION_PURPOSE_MAX_LENGTH.toLocaleString('en-GB')} characters or less`;
      }
      return errors;
    }
    case 'window': {
      if (!state.windowSameAsReportingPeriod) {
        errors['window-same'] = 'Select yes if the collection window will be the same as the reporting period';
      }
      if (state.windowSameAsReportingPeriod === 'no') {
        const openIssue = datePartsValidationIssue(state.opensDay, state.opensMonth, state.opensYear);
        const deadlineIssue = datePartsValidationIssue(state.deadlineDay, state.deadlineMonth, state.deadlineYear);
        const openIso = isoFromParts(state.opensDay, state.opensMonth, state.opensYear);
        const deadlineIso = isoFromParts(state.deadlineDay, state.deadlineMonth, state.deadlineYear);
        // One error per field, added in page order (open date, then deadline) because the error summary lists
        // them in insertion order. A past deadline is reported as such rather than as an ordering problem.
        if (openIssue) {
          Object.assign(errors, dateFieldMessages('open-date', openIssue));
        } else if (openIso !== options.savedOpenDate && !isDateTodayOrFuture(state.opensDay, state.opensMonth, state.opensYear)) {
          errors['open-date-future'] = 'Collection open date must be today or in the future';
        }
        if (deadlineIssue) {
          Object.assign(errors, dateFieldMessages('deadline-date', deadlineIssue));
        } else if (!isDateTodayOrFuture(state.deadlineDay, state.deadlineMonth, state.deadlineYear)) {
          errors['deadline-date-future'] = 'Submission deadline must be today or in the future';
        } else if (!openIssue && deadlineIso < openIso) {
          errors['deadline-date-order'] = 'Submission deadline must be after the date the collection opens';
        }
      }
      return errors;
    }
    case 'forms': {
      if (state.formModuleIds.length === 0) {
        errors['forms'] = 'Select at least one form to include in this data collection';
      }
      return errors;
    }
    case 'reportingLas': {
      if (!state.isSubmittedByAllLas) {
        errors['all-las'] = 'Select yes if you are requesting data from all organisations';
      }
      if (state.isSubmittedByAllLas === 'no' && state.selectedLaIds.length === 0) {
        errors['selected-las'] = 'Select at least one organisation';
      }
      return errors;
    }
    case 'reminders': {
      // With more than one reminder the same messages appeared verbatim per reminder, so the error
      // summary listed identical links pointing at different fields.
      const reminderSuffix = state.reminders.length > 1;
      state.reminders.forEach((reminder, index) => {
        if (!reminder.offsetType) {
          errors[`reminder-${index}-trigger`] = reminderSuffix ? `Select when to send reminder ${index + 1}` : 'Select when to send the reminder';
          return;
        }
        const isAfterOpen = reminder.offsetType === 'after_open';
        const days = reminder.days.trim();
        const forSuffix = reminderSuffix ? ` for reminder ${index + 1}` : '';
        if (!days) {
          errors[`reminder-${index}-days`] = isAfterOpen
            ? `Enter the number of days after the collection opens to send the reminder${forSuffix}`
            : `Enter the number of days before the submission deadline to send the reminder${forSuffix}`;
          return;
        }
        const isValidWholeNumber = /^\d+$/.test(days) && Number(days) >= 0 && Number(days) <= COLLECTION_MAX_REMINDER_DAYS;
        if (!isValidWholeNumber) {
          errors[`reminder-${index}-days`] = isAfterOpen
            ? `Number of days after the collection opens must be between 0 and ${COLLECTION_MAX_REMINDER_DAYS}${forSuffix}`
            : `Number of days before the submission deadline must be between 0 and ${COLLECTION_MAX_REMINDER_DAYS}${forSuffix}`;
        }
      });
      return errors;
    }
    default:
      return errors;
  }
};

export interface CollectionErrorListItem {
  targetName: string;
  text: string;
}

// Maps an error key to the real, focusable DOM id it must link to for the error summary to be
// GOV.UK-compliant (https://design-system.service.gov.uk/components/error-summary/#linking-from-the-error-summary-to-each-answer):
// a single-field error links to that field's own id; a radio-group error links to its first option
// (radios have no id of their own); a multi-part date links to its first sub-field. Shared by
// CollectionWizard (create) and CollectionEditor (edit) so both get the same, correct targets.
export const collectionErrorTarget = (key: string, state: CollectionFormState, formOptions: CollectionFormOptionDto[]): string => {
  switch (key) {
    case 'name':
      return 'admin-collections-create-name';
    case 'type':
      // First radio option in StepDetails' "admin-collections-create-type" group.
      return 'admin-collections-create-type-quarterly_report';
    case 'financialYear':
      return state.type === 'maturity_assessment' ? 'admin-collections-create-maturity-financial-year' : 'admin-collections-create-quarterly-financial-year';
    case 'quarter':
      return 'admin-collections-create-quarter';
    case 'purpose':
      return 'admin-collections-create-purpose';
    case 'window-same':
      // First radio option in StepWindow's "admin-collections-create-window-same" group.
      return 'admin-collections-create-window-same-yes';
    case 'open-date':
    case 'open-date-day':
    case 'open-date-invalid':
    case 'open-date-future':
      return 'admin-collections-create-open-date-day';
    case 'open-date-month':
      return 'admin-collections-create-open-date-month';
    case 'open-date-year':
      return 'admin-collections-create-open-date-year';
    case 'deadline-date':
    case 'deadline-date-day':
    case 'deadline-date-invalid':
    case 'deadline-date-future':
    case 'deadline-date-order':
      return 'admin-collections-create-deadline-date-day';
    case 'deadline-date-month':
      return 'admin-collections-create-deadline-date-month';
    case 'deadline-date-year':
      return 'admin-collections-create-deadline-date-year';
    case 'forms':
      // First checkbox in StepForms' "admin-collections-create-forms" group — depends on which of the type's form
      // options loaded. With none to link to, send focus to the step heading as 'submit' does: the group only carries
      // a testid, not an id, so linking to it would go nowhere.
      return formOptions[0] ? `admin-collections-create-forms-${formOptions[0].moduleId}` : 'collection-wizard-heading';
    case 'all-las':
      // First radio option in StepReportingLas' "admin-collections-create-all-las" group.
      return 'admin-collections-create-all-las-yes';
    case 'selected-las':
      // The "All" checkbox atop the organisation picker.
      return 'admin-collections-create-reporting-las-all';
    case 'submit':
      // No single field caused this (a server-side failure) — send focus to the step heading instead.
      return 'collection-wizard-heading';
    default:
      if (key.startsWith('reminder-') && key.endsWith('-trigger')) {
        const index = key.split('-')[1];
        return `admin-collections-create-reminder-${index}-trigger-after_open`;
      }
      if (key.startsWith('reminder-') && key.endsWith('-days')) {
        const index = key.split('-')[1];
        const reminder = state.reminders[Number(index)];
        return `admin-collections-create-reminder-${index}-${reminder?.offsetType || 'after_open'}-days`;
      }
      return key;
  }
};

export const buildCollectionErrorList = (state: CollectionFormState, formOptions: CollectionFormOptionDto[] = []): CollectionErrorListItem[] =>
  Object.entries(state.errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: collectionErrorTarget(key, state, formOptions), text: message as string }));
