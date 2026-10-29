import { isQuestionVisible } from '../../components/GovUKComponents';
import { formatGBP, isPenceAmount, MAX_MONEY_AMOUNT, MONEY_PATTERN, normalizeAmount, readAmount } from '../../helpers/currency';
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

// Whole pounds, £0 or more, at most MAX_MONEY_AMOUNT — the same rules the "Record funding spent" screens apply,
// shared through helpers/currency so the two cannot drift. The messages are this screen's own: they name the
// question, because a form question is its own subject where a funding amount belongs to an activity and quarter.
// Mirrors the backend MoneyAnswer.Validate message for message, including the "£" rule — an activity answer
// reaches the API as the raw string the user typed, so both sides can see the symbol.
//
// A question may carry a minimum, and the protocol's own name for the field in two forms (BLUM-8955): `sentence`
// starts a message ("Amount agreed must not include £"), `inline` reads mid-sentence ("… for the amount you have
// agreed"). Without them the label is quoted, as before.
const validateCurrencyAnswer = (question: FormQuestionDto, value: string): string | null => {
  const sentence = question.errorSubject ?? `“${question.label}”`;
  const inline = question.errorSubjectInline ?? `“${question.label}”`;
  const minimum = question.minValue ?? null;
  const belowMinimum = `Enter an amount of ${minimum} or more for ${inline}`;

  if (value.includes('£')) {
    return `${sentence} must not include £`;
  }

  const normalized = normalizeAmount(value);
  if (!MONEY_PATTERN.test(normalized)) {
    // Sign before shape, where there is a minimum to hold it to: "-5" is an amount below it, not a typo.
    if (minimum != null && normalized.startsWith('-') && MONEY_PATTERN.test(normalizeAmount(normalized.slice(1)))) {
      return belowMinimum;
    }
    // The example must be an amount the question would accept, so "0" goes when there is a minimum.
    return isPenceAmount(normalized)
      ? `${sentence} must be a whole number and not include pence`
      : `Enter a number for ${inline}, for example ${minimum != null && minimum >= 1 ? '1500' : '0 or 1500'}`;
  }

  // isFinite guards the digit string long enough to reach Infinity before the comparison — "too big" is what
  // that is.
  const amount = Number(normalized);
  if (!Number.isFinite(amount) || amount > MAX_MONEY_AMOUNT) {
    return `${sentence} must be £1,000,000,000 or less`;
  }

  return minimum != null && amount < minimum ? belowMinimum : null;
};

export interface ValidateAnswersOptions {
  // The quarterly report's "Save and come back later" skips required enforcement but still blocks on
  // type/range errors so garbage never persists.
  enforceRequired?: boolean;
}

// Validates dynamic answers in the create/edit flow. Mirrors the backend ActivityAnswerValidator: only visible,
// active questions are considered; required questions must be answered (with the protocol's own message where one
// is seeded); free text must stay inside the protocol's character limit; numbers must parse (and stay within the
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
        errors[question.code] = question.errorMessage ?? `Enter an answer for “${question.label}”`;
      }
      return;
    }

    if (question.maxLength != null && value.length > question.maxLength) {
      errors[question.code] = question.maxLengthErrorMessage ?? `“${question.label}” must be ${question.maxLength} characters or less`;
      return;
    }

    if (question.questionType === 'Number') {
      const numberError = validateNumberAnswer(question, value);
      if (numberError) {
        errors[question.code] = numberError;
      }
    }

    if (question.questionType === 'Currency') {
      const currencyError = validateCurrencyAnswer(question, value);
      if (currencyError) {
        errors[question.code] = currencyError;
      }
    }
  });

  return errors;
};

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

  // Check your answers and the read-only view show a money answer the way every other amount in the service is
  // shown. A value that failed validation could never have been saved, so the fallback is only reachable for a
  // row written before the question became a Currency one, or one saved while a misplaced separator was still
  // stripped (BLUM-8955). Mirrors ReportAnswerPresenter.FormatCurrency.
  if (question.questionType === 'Currency') {
    const amount = readAmount(value);
    return [amount != null ? formatGBP(amount) : value];
  }

  return [value];
};
