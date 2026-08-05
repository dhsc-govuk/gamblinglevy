// Shared form state for the create and edit activity wizards (BLUM redesign). Structural fields (category,
// type, status, name) are first-class; the per-category "detail" questions are schema-driven (answers); the
// key results are standard per-type checkbox selections plus optional custom "Other" free-text rows.
// Changing the category resets the type and any detail answers, since a different category surfaces a
// different schema and type list; changing the type resets the key-result selections (options are per-type).

export interface ActivityFormState {
  title: string;
  categoryId: string;
  taskId: string;
  customTaskText: string;
  // Dynamic question answers keyed by question code.
  answers: Record<string, string>;
  // Standard key-result selections (taxonomy key-result codes) for the chosen type.
  keyResultCodes: string[];
  // Custom "Other" free-text key results.
  expectedResults: string[];
  // Whether the standard-options screen's "Other" checkbox is selected. This is separate from the text so an
  // intentionally selected but blank conditional field can be validated.
  otherExpectedResultSelected: boolean;
  // User-chosen lifecycle status: '' | planned | active | closed | finished.
  status: string;
  startMonth: string;
  startYear: string;
  errors: Record<string, string>;
  submitAttempts: number;
}

export const initialActivityFormState: ActivityFormState = {
  title: '',
  categoryId: '',
  taskId: '',
  customTaskText: '',
  answers: {},
  keyResultCodes: [],
  expectedResults: [''],
  otherExpectedResultSelected: false,
  status: '',
  startMonth: '',
  startYear: '',
  errors: {},
  submitAttempts: 0,
};

export type ActivityFormAction =
  | { type: 'SET_TITLE'; value: string }
  | { type: 'SET_CATEGORY'; value: string }
  | { type: 'SET_TASK'; value: string; isOther: boolean }
  | { type: 'SET_CUSTOM_TASK'; value: string }
  | { type: 'SET_ANSWER'; code: string; value: string }
  | { type: 'SET_KEY_RESULT_CODES'; values: string[] }
  | { type: 'SET_EXPECTED_RESULTS'; values: string[] }
  | { type: 'SET_OTHER_EXPECTED_RESULT_SELECTED'; value: boolean }
  | { type: 'SET_STATUS'; value: string }
  | { type: 'SET_START_MONTH'; value: string }
  | { type: 'SET_START_YEAR'; value: string }
  | { type: 'SET_ERRORS'; errors: Record<string, string> }
  | { type: 'BUMP_ATTEMPTS' }
  | { type: 'HYDRATE'; state: Partial<ActivityFormState> };

export function activityFormReducer(state: ActivityFormState, action: ActivityFormAction): ActivityFormState {
  switch (action.type) {
    case 'SET_TITLE':
      return { ...state, title: action.value };
    case 'SET_CATEGORY':
      return {
        ...state,
        categoryId: action.value,
        taskId: '',
        customTaskText: '',
        answers: {},
        keyResultCodes: [],
        expectedResults: [''],
        otherExpectedResultSelected: false,
      };
    case 'SET_TASK':
      // Standard key-result options are per-type, so changing the type clears any selections and custom texts.
      // The custom "Other" name only survives while an "Other ..." type is selected — otherwise a stale custom
      // text would be shown in the name hint and submitted against a standard type.
      return {
        ...state,
        taskId: action.value,
        keyResultCodes: [],
        expectedResults: [''],
        otherExpectedResultSelected: false,
        customTaskText: action.isOther ? state.customTaskText : '',
      };
    case 'SET_CUSTOM_TASK':
      return { ...state, customTaskText: action.value };
    case 'SET_ANSWER':
      return { ...state, answers: { ...state.answers, [action.code]: action.value } };
    case 'SET_KEY_RESULT_CODES':
      return { ...state, keyResultCodes: action.values };
    case 'SET_EXPECTED_RESULTS':
      return { ...state, expectedResults: action.values };
    case 'SET_OTHER_EXPECTED_RESULT_SELECTED':
      return { ...state, otherExpectedResultSelected: action.value };
    case 'SET_STATUS':
      return action.value === 'active' ? { ...state, status: action.value } : { ...state, status: action.value, startMonth: '', startYear: '' };
    case 'SET_START_MONTH':
      return { ...state, startMonth: action.value };
    case 'SET_START_YEAR':
      return { ...state, startYear: action.value };
    case 'SET_ERRORS':
      return { ...state, errors: action.errors };
    case 'BUMP_ATTEMPTS':
      return { ...state, submitAttempts: state.submitAttempts + 1 };
    case 'HYDRATE':
      return { ...state, ...action.state };
    default:
      return state;
  }
}
