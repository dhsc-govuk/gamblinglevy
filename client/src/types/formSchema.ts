// Shared activity-record form-schema types, used by both the DHSC Form Designer (authoring) and the LA-facing
// activity-creation flow (consumption). FormQuestionDto is a structural superset of the
// GovUKDynamicQuestionRenderer's FormQuestion (it adds sectionId), so the same objects render directly.

// Mirrors Domain.Forms.QuestionTypes. 'Currency' is a whole-pound money amount rendered with a '£' prefix
// (BLUM-7860) — kept distinct from 'Number', whose answers are counts and percentages that must not gain one.
export type QuestionType = 'Text' | 'Textarea' | 'Number' | 'Radio' | 'Checkbox' | 'Select' | 'Currency';

export const QUESTION_TYPES: QuestionType[] = ['Text', 'Textarea', 'Number', 'Radio', 'Checkbox', 'Select', 'Currency'];

export const isChoiceType = (type: string): boolean => type === 'Radio' || type === 'Checkbox' || type === 'Select';

export interface FormQuestionOptionDto {
  value: string;
  label: string;
  displayOrder: number;
}

export interface FormQuestionDto {
  id: string;
  sectionId: string;
  code: string;
  label: string;
  hint: string | null;
  helpText: string | null;
  helpTextSummary: string | null;
  questionType: string;
  isRequired: boolean;
  isActive: boolean;
  displayOrder: number;
  conditionalQuestionCode: string | null;
  conditionalValue: string | null;
  options: FormQuestionOptionDto[];
  // Numeric metadata from the quarterly-report Question Protocol (BLUM-6611): whole-number range bounds
  // and a display suffix ("%" for percentages). Absent on activity-record schema questions, except that a
  // Currency question may carry minValue — the smallest amount it accepts (BLUM-8955).
  minValue?: number | null;
  maxValue?: number | null;
  suffix?: string | null;
  // The protocol's name for a Currency field, which is not the question (BLUM-8955): errorSubject starts a
  // message ("Amount agreed"), errorSubjectInline reads mid-sentence ("the amount you have agreed").
  errorSubject?: string | null;
  errorSubjectInline?: string | null;
  // Question Protocol copy (BLUM-7620): the message shown when a required question is left blank, the free-text
  // character limit (which turns a Textarea into a GDS character count) and the over-limit message.
  errorMessage?: string | null;
  maxLength?: number | null;
  maxLengthErrorMessage?: string | null;
  // A checkbox option that clears every other selection when ticked, and is cleared by any of them — the maturity
  // assessment's "None" (BLUM-7855). Absent on activity-record and quarterly questions.
  exclusiveOptionValue?: string | null;
}

export interface FormSectionDto {
  id: string;
  categoryId: string | null;
  // Optional activity-type scoping (BLUM-6933): when set, the section only applies while that type is selected.
  activityTaskId: string | null;
  title: string;
  description: string | null;
  displayOrder: number;
  isActive: boolean;
}

export interface FormSchemaDto {
  sections: FormSectionDto[];
  questions: FormQuestionDto[];
}
