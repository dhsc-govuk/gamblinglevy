import axios from 'axios';
import type { FormQuestionDto, FormSectionDto } from '../../../types/formSchema';

export interface SectionInput {
  title: string;
  description: string | null;
  categoryId: string | null;
}

export interface QuestionOptionInput {
  value: string;
  label: string;
  displayOrder: number;
}

export interface CreateQuestionInput {
  sectionId: string;
  code: string;
  label: string;
  questionType: string;
  isRequired: boolean;
  hint: string | null;
  helpText: string | null;
  helpTextSummary: string | null;
  conditionalQuestionCode: string | null;
  conditionalValue: string | null;
  options: QuestionOptionInput[];
}

// `code?: never` mirrors views/Admin/Forms/mutations.ts. UpdateQuestionRequestDto does not declare a
// code, and Omit<> alone does not stop a CreateQuestionInput being passed here — excess-property
// checking only fires on object literals, so the moment a caller builds the payload in a helper the
// code would be sent and the API's unmapped-member rejection would 400 the save (BLUM-7240).
export type UpdateQuestionInput = Omit<CreateQuestionInput, 'code'> & { isActive: boolean; code?: never };

export const createSection = (input: SectionInput) =>
  axios.post<FormSectionDto>('admin/activity-form/sections', input);

export const updateSection = (id: string, input: SectionInput) =>
  axios.put<FormSectionDto>(`admin/activity-form/sections/${id}`, input);

export const deleteSection = (id: string) => axios.delete(`admin/activity-form/sections/${id}`);

export const reorderSections = (orderedIds: string[]) =>
  axios.post('admin/activity-form/sections/reorder', { orderedIds });

export const createQuestion = (input: CreateQuestionInput) =>
  axios.post<FormQuestionDto>('admin/activity-form/questions', input);

export const updateQuestion = (id: string, input: UpdateQuestionInput) =>
  axios.put<FormQuestionDto>(`admin/activity-form/questions/${id}`, input);

export const deleteQuestion = (id: string) => axios.delete(`admin/activity-form/questions/${id}`);

export const reorderQuestions = (sectionId: string, orderedIds: string[]) =>
  axios.post('admin/activity-form/questions/reorder', { sectionId, orderedIds });
