import type { CreateCollectionInput, UpdateCollectionInput } from './mutations';
import type { CollectionDto, CollectionFormOptionDto } from './queries';

// Form state for the add-collection wizard. Backend enum string values are used verbatim for
// `type` and reminder `offsetType` so `buildCreateInput` is a thin mapping with no extra lookup tables.

export type CollectionFormType = '' | 'quarterly_report' | 'maturity_assessment';
export type YesNo = '' | 'yes' | 'no';
export type ReminderOffset = '' | 'after_open' | 'before_deadline';

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
    case 'SET_TYPE':
      // Quarter only applies to quarterly reports — clear it for any other type.
      if (action.value !== 'quarterly_report') {
        const errors = { ...state.errors };
        delete errors['quarter'];
        return {
          ...state,
          type: action.value,
          quarter: '',
          errors,
        };
      }
      return {
        ...state,
        type: action.value,
      };
    case 'SET_WINDOW_SAME':
      // When the window mirrors the reporting period the explicit open/deadline dates are unused.
      if (action.value === 'yes') {
        const errors = { ...state.errors };
        delete errors['window-same'];
        delete errors['open-date'];
        delete errors['open-date-day'];
        delete errors['open-date-month'];
        delete errors['open-date-year'];
        delete errors['deadline-date'];
        delete errors['deadline-date-day'];
        delete errors['deadline-date-month'];
        delete errors['deadline-date-year'];
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
      return { ...state, reminders: reminders.length > 0 ? reminders : [blankReminder()] };
    }
    case 'SET_REMINDER': {
      const reminderIndex = state.reminders.findIndex((reminder) => reminder.id === action.id);
      const errors = { ...state.errors };
      if (reminderIndex >= 0 && action.patch.days !== undefined && /^\d+$/.test(action.patch.days.trim())) {
        delete errors[`reminder-${reminderIndex}-days`];
      }
      if (reminderIndex >= 0 && action.patch.offsetType) {
        delete errors[`reminder-${reminderIndex}-trigger`];
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

export type DatePartValidationIssue = 'missing' | 'day' | 'month' | 'year';

// All three day/month/year parts present as plain integers, within the allowed ranges, and combinable
// into a valid calendar date.
export const datePartsValidationIssue = (day: string, month: string, year: string): DatePartValidationIssue | null => {
  if (!day || !month || !year) {
    return 'missing';
  }
  if (!/^\d{1,2}$/.test(day)) {
    return 'day';
  }
  if (!/^\d{1,2}$/.test(month)) {
    return 'month';
  }
  if (!/^\d{4}$/.test(year)) {
    return 'year';
  }
  const d = Number(day);
  const m = Number(month);
  const y = Number(year);
  if (d < 1 || d > 31) {
    return 'day';
  }
  if (m < 1 || m > 12) {
    return 'month';
  }
  if (y < 1970) {
    return 'year';
  }
  const date = new Date(Date.UTC(y, m - 1, d));
  if (date.getUTCFullYear() !== y || date.getUTCMonth() !== m - 1 || date.getUTCDate() !== d) {
    return 'day';
  }
  return null;
};

export const datePartsValid = (day: string, month: string, year: string): boolean => datePartsValidationIssue(day, month, year) === null;

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
export const buildCreateInput = (state: CollectionFormState, saveAsDraft: boolean): CreateCollectionInput => {
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
export function buildUpdateInput(state: CollectionFormState, saveAsDraft: boolean): UpdateCollectionInput {
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
    reminders: state.reminders
      .filter((reminder) => reminder.offsetType)
      .map((reminder) => ({
        offsetType: reminder.offsetType as Exclude<ReminderOffset, ''>,
        days: Number(reminder.days || '0'),
      })),
  };
}

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
    case 'window-same':
      // First radio option in StepWindow's "admin-collections-create-window-same" group.
      return 'admin-collections-create-window-same-yes';
    case 'open-date':
      return 'admin-collections-create-open-date-day';
    case 'open-date-day':
      return 'admin-collections-create-open-date-day';
    case 'open-date-month':
      return 'admin-collections-create-open-date-month';
    case 'open-date-year':
      return 'admin-collections-create-open-date-year';
    case 'deadline-date':
      return 'admin-collections-create-deadline-date-day';
    case 'deadline-date-day':
      return 'admin-collections-create-deadline-date-day';
    case 'deadline-date-month':
      return 'admin-collections-create-deadline-date-month';
    case 'deadline-date-year':
      return 'admin-collections-create-deadline-date-year';
    case 'forms':
      // First checkbox in StepForms' "admin-collections-create-forms" group — depends on which form
      // options loaded, so falls back to the group's own testid if the options haven't loaded yet.
      return formOptions[0] ? `admin-collections-create-forms-${formOptions[0].moduleId}` : 'admin-collections-create-forms';
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
