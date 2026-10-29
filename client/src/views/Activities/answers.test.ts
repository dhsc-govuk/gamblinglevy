import { describe, expect, it, vi } from 'vitest';
import type { FormQuestionDto } from '../../types/formSchema';
import { validateAnswers } from './answers';

// Visibility is not what these tests are about, and the real isQuestionVisible lives in a component module whose
// barrel pulls in the whole UI kit. None of the questions below has a condition, so every one is visible.
vi.mock('../../components/GovUKComponents', () => ({ isQuestionVisible: () => true }));

// The messages a Currency answer is refused with. An activity answer reaches the API as the string the user typed
// and MoneyAnswer.Validate refuses it with the same wording, so these cases and messages are copied from
// api/tests/Application.Tests.Unit/Validation/MoneyAnswerTests.cs. Nothing links the two suites: a rule or a
// message changed on one side has to be copied to the other by hand.

const currencyQuestion = (overrides: Partial<FormQuestionDto> = {}): FormQuestionDto => ({
  id: 'question-1',
  sectionId: 'section-1',
  code: 'AMOUNT',
  label: 'How much?',
  hint: null,
  helpText: null,
  helpTextSummary: null,
  questionType: 'Currency',
  isRequired: false,
  isActive: true,
  displayOrder: 1,
  conditionalQuestionCode: null,
  conditionalValue: null,
  options: [],
  ...overrides,
});

// The joint partnership's JP_COMMITTED as it is seeded: a minimum of 1 and the protocol's name for the field.
const amountAgreed = currencyQuestion({ minValue: 1, errorSubject: 'Amount agreed', errorSubjectInline: 'the amount you have agreed' });

const errorFor = (question: FormQuestionDto, value: string): string | undefined => validateAnswers([question], { [question.code]: value })[question.code];

describe('validateAnswers — a Currency question with a minimum and a subject', () => {
  it.each(['1', '1500', '1,500', '25 000', '25 000', '1,000,000', '1000000000', '1,000,000,000'])('accepts %j', (value) => {
    expect(errorFor(amountAgreed, value)).toBeUndefined();
  });

  it.each(['10.50', '5,10', '0,10', '0,100', '1,5000', '1,500.50', '1,500,50', '25 000,5'])('refuses the decimal %j', (value) => {
    expect(errorFor(amountAgreed, value)).toBe('Amount agreed must be a whole number and not include pence');
  });

  it.each(['0', '000', '-5', '- 5', '-1,500'])('refuses %j as below the minimum', (value) => {
    expect(errorFor(amountAgreed, value)).toBe('Enter an amount of 1 or more for the amount you have agreed');
  });

  it.each(['lots', '5 10', '12,34,56', '1,,500', ',500', '1,500,', '.5', '5.'])('refuses %j as not a number', (value) => {
    expect(errorFor(amountAgreed, value)).toBe('Enter a number for the amount you have agreed, for example 1500');
  });

  it.each(['£5', '£1,500', '5£'])('refuses the typed pound sign in %j', (value) => {
    expect(errorFor(amountAgreed, value)).toBe('Amount agreed must not include £');
  });

  it.each(['1000000001', '1,000,000,001', '99999999999999999999999999999999999999'])('refuses %j as over the cap', (value) => {
    expect(errorFor(amountAgreed, value)).toBe('Amount agreed must be £1,000,000,000 or less');
  });
});

describe('validateAnswers — a Currency question with no minimum and no subject', () => {
  const howMuch = currencyQuestion();

  it('accepts £0', () => {
    expect(errorFor(howMuch, '0')).toBeUndefined();
  });

  it.each([
    ['5,10', '“How much?” must be a whole number and not include pence'],
    ['£5', '“How much?” must not include £'],
    ['lots', 'Enter a number for “How much?”, for example 0 or 1500'],
    ['-5', 'Enter a number for “How much?”, for example 0 or 1500'],
    ['1000000001', '“How much?” must be £1,000,000,000 or less'],
  ])('refuses %j by quoting the label', (value, expected) => {
    expect(errorFor(howMuch, value)).toBe(expected);
  });
});
