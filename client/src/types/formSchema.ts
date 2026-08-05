// Shared activity-record form-schema types, used by both the DHSC Form Designer (authoring) and the LA-facing
// activity-creation flow (consumption). FormQuestionDto is a structural superset of the
// GovUKDynamicQuestionRenderer's FormQuestion (it adds sectionId), so the same objects render directly.

export type QuestionType = 'Text' | 'Textarea' | 'Number' | 'Radio' | 'Checkbox' | 'Select';

export const QUESTION_TYPES: QuestionType[] = ['Text', 'Textarea', 'Number', 'Radio', 'Checkbox', 'Select'];

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
  // and a display suffix ("%" for percentages). Absent on activity-record schema questions.
  minValue?: number | null;
  maxValue?: number | null;
  suffix?: string | null;
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
