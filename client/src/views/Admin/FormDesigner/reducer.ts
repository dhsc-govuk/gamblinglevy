import { isChoiceType, type QuestionType } from '../../../types/formSchema';

export interface QuestionOptionState {
  value: string;
  label: string;
}

export interface QuestionFormState {
  code: string;
  label: string;
  questionType: QuestionType;
  sectionId: string;
  hint: string;
  helpText: string;
  helpTextSummary: string;
  isRequired: boolean;
  isActive: boolean;
  conditionalEnabled: boolean;
  conditionalQuestionCode: string;
  conditionalValue: string;
  options: QuestionOptionState[];
  errors: Record<string, string>;
  submitAttempts: number;
}

export const createInitialQuestionFormState = (sectionId = ''): QuestionFormState => ({
  code: '',
  label: '',
  questionType: 'Text',
  sectionId,
  hint: '',
  helpText: '',
  helpTextSummary: '',
  isRequired: false,
  isActive: true,
  conditionalEnabled: false,
  conditionalQuestionCode: '',
  conditionalValue: '',
  options: [],
  errors: {},
  submitAttempts: 0,
});

type TextField = 'code' | 'label' | 'sectionId' | 'hint' | 'helpText' | 'helpTextSummary' | 'conditionalQuestionCode' | 'conditionalValue';

export type QuestionFormAction =
  | { type: 'SET_FIELD'; field: TextField; value: string }
  | { type: 'SET_TYPE'; value: QuestionType }
  | { type: 'SET_FLAG'; field: 'isRequired' | 'isActive'; value: boolean }
  | { type: 'TOGGLE_CONDITIONAL'; enabled: boolean }
  | { type: 'ADD_OPTION' }
  | { type: 'REMOVE_OPTION'; index: number }
  | { type: 'UPDATE_OPTION'; index: number; field: 'value' | 'label'; value: string }
  | { type: 'MOVE_OPTION'; index: number; direction: 'up' | 'down' }
  | { type: 'SET_ERRORS'; errors: Record<string, string> }
  | { type: 'BUMP_ATTEMPTS' }
  | { type: 'HYDRATE'; state: Partial<QuestionFormState> };

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
    case 'SET_FIELD':
      return { ...state, [action.field]: action.value };
    case 'SET_TYPE': {
      const becomesChoice = isChoiceType(action.value);
      const wasChoice = isChoiceType(state.questionType);
      let options = state.options;
      if (becomesChoice && !wasChoice) {
        options = [{ value: '', label: '' }];
      } else if (!becomesChoice) {
        options = [];
      }
      return { ...state, questionType: action.value, options };
    }
    case 'SET_FLAG':
      return { ...state, [action.field]: action.value };
    case 'TOGGLE_CONDITIONAL':
      return action.enabled ? { ...state, conditionalEnabled: true } : { ...state, conditionalEnabled: false, conditionalQuestionCode: '', conditionalValue: '' };
    case 'ADD_OPTION':
      return { ...state, options: [...state.options, { value: '', label: '' }] };
    case 'REMOVE_OPTION':
      return { ...state, options: state.options.filter((_, index) => index !== action.index) };
    case 'UPDATE_OPTION':
      return {
        ...state,
        options: state.options.map((option, index) => (index === action.index ? { ...option, [action.field]: action.value } : option)),
      };
    case 'MOVE_OPTION':
      return { ...state, options: move(state.options, action.index, action.direction) };
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

// Validates the question editor. Errors are keyed to the matching input id so the ErrorSummary can focus them.
export function validateQuestion(state: QuestionFormState): Record<string, string> {
  const errors: Record<string, string> = {};

  if (!state.sectionId) {
    errors['form-question-section'] = 'Select a section';
  }
  if (!state.code.trim()) {
    errors['form-question-code'] = 'Enter a question code';
  } else if (!/^[A-Z0-9_]+$/.test(state.code.trim())) {
    errors['form-question-code'] = 'A code can only contain capital letters, numbers and underscores';
  }
  if (!state.label.trim()) {
    errors['form-question-label'] = 'Enter a question label';
  }

  if (isChoiceType(state.questionType)) {
    if (state.options.length === 0) {
      errors['form-question-options'] = 'Add at least one option';
    } else if (state.options.some((option) => !option.value.trim() || !option.label.trim())) {
      errors['form-question-options'] = 'Enter a value and label for every option';
    } else {
      const values = state.options.map((option) => option.value.trim());
      if (new Set(values).size !== values.length) {
        errors['form-question-options'] = 'Each option must have a unique value';
      }
    }
  }

  if (state.conditionalEnabled) {
    if (!state.conditionalQuestionCode) {
      errors['form-question-conditional-parent'] = 'Select the question this depends on';
    }
    if (!state.conditionalValue) {
      errors['form-question-conditional-value'] = 'Select the answer that reveals this question';
    }
  }

  return errors;
}
