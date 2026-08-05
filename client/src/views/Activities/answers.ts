import { isQuestionVisible } from '../../components/GovUKComponents';
import type { FormQuestionDto } from '../../types/formSchema';
import type { ActivityAnswerInput } from './mutations';

// Numeric checks for a Number answer. Questions carrying the quarterly-report protocol metadata
// (suffix/min/max) are whole numbers with ranges — messages mirror the backend SubmissionAnswerValidator;
// plain activity-record numbers keep the legacy "must be a number" check.
const validateNumberAnswer = (question: FormQuestionDto, value: string): string | null => {
  const isPercentage = question.suffix === '%';
  const hasRange = question.minValue != null || question.maxValue != null;

  if (!isPercentage && !hasRange) {
    return Number.isNaN(Number(value)) ? `“${question.label}” must be a number` : null;
  }

  if (!/^-?\d+$/.test(value)) {
    return isPercentage ? 'Enter a percentage, like 25' : `“${question.label}” must be a whole number`;
  }

  const number = Number(value);
  if (question.minValue != null && number < question.minValue) {
    return isPercentage ? 'Enter a percentage between 0 and 100' : `Enter a number of ${question.minValue} or more`;
  }

  if (question.maxValue != null && number > question.maxValue) {
    return isPercentage ? 'Enter a percentage between 0 and 100' : `Enter a number of ${question.maxValue} or less`;
  }

  return null;
};

export interface ValidateAnswersOptions {
  // The quarterly report's "Save and come back later" skips required enforcement but still blocks on
  // type/range errors so garbage never persists.
  enforceRequired?: boolean;
}

// Validates dynamic answers in the create/edit flow. Mirrors the backend ActivityAnswerValidator: only visible,
// active questions are considered; required questions must be answered; numbers must parse (and stay within the
// protocol's whole-number ranges when min/max/suffix metadata is present). Keyed by question code.
export const validateAnswers = (questions: FormQuestionDto[], answers: Record<string, string>, { enforceRequired = true }: ValidateAnswersOptions = {}): Record<string, string> => {
  const errors: Record<string, string> = {};

  questions.forEach((question) => {
    if (!question.isActive || !isQuestionVisible(question, answers)) {
      return;
    }

    const value = (answers[question.code] ?? '').trim();

    if (!value) {
      if (enforceRequired && question.isRequired) {
        errors[question.code] = `Enter an answer for “${question.label}”`;
      }
      return;
    }

    if (question.questionType === 'Number') {
      const numberError = validateNumberAnswer(question, value);
      if (numberError) {
        errors[question.code] = numberError;
      }
    }
  });

  return errors;
};

// People and issues requires at least one related issue. Keep the client override while older API instances can
// still return the Help question as optional during a rolling deployment.
export const validateActivityAnswers = (questions: FormQuestionDto[], answers: Record<string, string>): Record<string, string> =>
  validateAnswers(
    questions.map((question) => (question.code.endsWith('_RELATED_ISSUES') ? { ...question, isRequired: true } : question)),
    answers
  );

// The answers to submit: visible, active questions with a non-empty value. Hidden/inactive/blank answers are
// pruned so stale or orphaned responses are never persisted.
export const collectAnswers = (questions: FormQuestionDto[], answers: Record<string, string>): ActivityAnswerInput[] =>
  questions
    .filter((question) => question.isActive && isQuestionVisible(question, answers))
    .map((question) => ({ questionCode: question.code, value: (answers[question.code] ?? '').trim() }))
    .filter((answer) => answer.value.length > 0);

// Dynamic text/select controls use the lower-cased question code as their DOM id. Radio and checkbox groups
// use that code as a prefix, so their error links must target the first real input rather than a missing base id.
export const questionErrorTargetId = (question: FormQuestionDto): string => {
  const baseId = question.code.toLowerCase();
  const questionType = String(question.questionType);
  if (!['Radio', '1', 'Checkbox', '2'].includes(questionType)) {
    return baseId;
  }

  const firstOption = [...question.options].sort((a, b) => a.displayOrder - b.displayOrder)[0];
  return firstOption ? `${baseId}-${firstOption.value}` : baseId;
};

// Human-readable display of an answer: choice values mapped to their option labels, checkbox to a list.
export const resolveAnswerDisplay = (question: FormQuestionDto, value: string | undefined): string[] => {
  if (!value) {
    return [];
  }

  const labelFor = (optionValue: string): string => question.options.find((option) => option.value === optionValue)?.label ?? optionValue;

  if (question.questionType === 'Checkbox') {
    return value
      .split(',')
      .map((part) => part.trim())
      .filter(Boolean)
      .map(labelFor);
  }

  if (question.questionType === 'Radio' || question.questionType === 'Select') {
    return [labelFor(value)];
  }

  return [value];
};
