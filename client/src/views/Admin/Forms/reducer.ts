import { isDcChoiceType, type DcQuestionType } from './queries';
import type { CreateQuestionInput, QuestionOptionInput } from './mutations';

// Reducer for the data-collection question editor. Mirrors the activity Form Designer's reducer but
// drops the conditional-rule fields (data-collection questions have no conditional logic). Error keys
// match the matching input id so the ErrorSummary can focus the offending field.

export interface QuestionOptionState {
  value: string;
  label: string;
}

export interface QuestionFormState {
  code: string;
  label: string;
  questionType: DcQuestionType;
  sectionId: string;
  hint: string;
  helpText: string;
  isRequired: boolean;
  options: QuestionOptionState[];
  errors: Record<string, string>;
}

type TextField = 'code' | 'label' | 'sectionId' | 'hint' | 'helpText';
type FieldValue = string | boolean | DcQuestionType;

export const createInitialQuestionFormState = (sectionId = ''): QuestionFormState => ({
  code: '',
  label: '',
  questionType: 'Text',
  sectionId,
  hint: '',
  helpText: '',
  isRequired: false,
  options: [],
  errors: {},
});

export type QuestionFormAction =
  | { type: 'SET_FIELD'; field: TextField | 'questionType' | 'isRequired'; value: FieldValue }
  | { type: 'ADD_OPTION' }
  | { type: 'REMOVE_OPTION'; index: number }
  | { type: 'UPDATE_OPTION'; index: number; field: 'value' | 'label'; value: string }
  | { type: 'REORDER_OPTIONS'; index: number; direction: 'up' | 'down' }
  | { type: 'INIT'; state: Partial<QuestionFormState> }
  | { type: 'SET_ERRORS'; errors: Record<string, string> };

const move = <T>(items: T[], index: number, direction: 'up' | 'down'): T[] => {
  const target = direction === 'up' ? index - 1 : index + 1;
  if (target < 0 || target >= items.length) {
    return items;
  }
  const next = [...items];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
};

export function questionFormReducer(state: QuestionFormState, action: QuestionFormAction): QuestionFormState {
  switch (action.type) {
    case 'SET_FIELD': {
      if (action.field === 'questionType') {
        const nextType = action.value as DcQuestionType;
        const becomesChoice = isDcChoiceType(nextType);
        const wasChoice = isDcChoiceType(state.questionType);
        let options = state.options;
        if (becomesChoice && !wasChoice) {
          options = [{ value: '', label: '' }];
        } else if (!becomesChoice) {
          options = [];
        }
        return { ...state, questionType: nextType, options };
      }
      return { ...state, [action.field]: action.value };
    }
    case 'ADD_OPTION':
      return { ...state, options: [...state.options, { value: '', label: '' }] };
    case 'REMOVE_OPTION':
      return { ...state, options: state.options.filter((_, index) => index !== action.index) };
    case 'UPDATE_OPTION':
      return {
        ...state,
        options: state.options.map((option, index) => (index === action.index ? { ...option, [action.field]: action.value } : option)),
      };
    case 'REORDER_OPTIONS':
      return { ...state, options: move(state.options, action.index, action.direction) };
    case 'INIT':
      return { ...createInitialQuestionFormState(), ...action.state };
    case 'SET_ERRORS':
      return { ...state, errors: action.errors };
    default:
      return state;
  }
}

// Validates the question editor. Error keys mirror the matching input id for ErrorSummary focusing.
export function validateQuestion(state: QuestionFormState, isEdit: boolean): Record<string, string> {
  const errors: Record<string, string> = {};

  if (!isEdit) {
    if (!state.code.trim()) {
      errors['admin-forms-question-code'] = 'Enter a question code';
    } else if (!/^[A-Z0-9_]+$/.test(state.code.trim())) {
      errors['admin-forms-question-code'] = 'A code can only contain capital letters, numbers and underscores';
    }
  }

  if (!state.label.trim()) {
    errors['admin-forms-question-label'] = 'Enter a question label';
  }

  if (isDcChoiceType(state.questionType)) {
    if (state.options.length === 0) {
      errors['admin-forms-question-options'] = 'Add at least one option';
    } else if (state.options.some((option) => !option.value.trim() || !option.label.trim())) {
      errors['admin-forms-question-options'] = 'Enter a value and label for every option';
    } else {
      const values = state.options.map((option) => option.value.trim());
      if (new Set(values).size !== values.length) {
        errors['admin-forms-question-options'] = 'Each option must have a unique value';
      }
    }
  }

  return errors;
}

// Builds the API payload from the editor state. Options are only sent for choice-type questions.
export function buildQuestionInput(state: QuestionFormState): CreateQuestionInput {
  const options: QuestionOptionInput[] = isDcChoiceType(state.questionType) ? state.options.map((option) => ({ value: option.value.trim(), label: option.label.trim() })) : [];

  return {
    sectionId: state.sectionId ? state.sectionId : null,
    code: state.code.trim(),
    label: state.label.trim(),
    questionType: state.questionType,
    isRequired: state.isRequired,
    hint: state.hint.trim() ? state.hint.trim() : null,
    helpText: state.helpText.trim() ? state.helpText.trim() : null,
    options,
  };
}
