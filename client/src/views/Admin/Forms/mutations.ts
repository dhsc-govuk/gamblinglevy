import axios from 'axios';
import type { DcQuestionType, FormModuleDto, FormModuleWithChildrenDto } from './queries';

const BASE = 'admin/data-collection-forms';

// ---- Module inputs ----------------------------------------------------------

export interface CreateFormModuleInput {
  code: string;
  name: string;
  description: string | null;
}

export interface UpdateFormModuleInput {
  name: string;
  description: string | null;
}

// ---- Section inputs ---------------------------------------------------------

export interface SectionInput {
  title: string;
  description: string | null;
  helpText: string | null;
}

// ---- Question inputs --------------------------------------------------------

export interface QuestionOptionInput {
  value: string;
  label: string;
}

export interface CreateQuestionInput {
  sectionId: string | null;
  code: string;
  label: string;
  questionType: DcQuestionType;
  isRequired: boolean;
  hint: string | null;
  helpText: string | null;
  options: QuestionOptionInput[];
}

// The question code is immutable after creation, so it is omitted from updates.
//
// `code?: never` is not decoration. Omit<> alone does not stop a CreateQuestionInput being passed here —
// excess-property checking only fires on object literals, and callers pass a variable built by the shared
// QuestionForm. That silently sent `code`, which the API rejects as an unmapped member (BLUM-7240). This
// makes the same mistake a tsc error instead, which is the only automated gate the client has.
export type UpdateQuestionInput = Omit<CreateQuestionInput, 'code'> & { code?: never };

// ---- Module mutations -------------------------------------------------------

export const createFormModule = (input: CreateFormModuleInput) => axios.post<FormModuleDto>(`${BASE}/modules`, input);

export const updateFormModule = (moduleId: string, input: UpdateFormModuleInput) => axios.put<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}`, input);

export const deleteFormModule = (moduleId: string) => axios.delete(`${BASE}/modules/${moduleId}`);

export const publishFormModule = (moduleId: string) => axios.post<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}/publish`);

// ---- Section mutations ------------------------------------------------------

export const createSection = (moduleId: string, input: SectionInput) => axios.post<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}/sections`, input);

export const updateSection = (moduleId: string, sectionId: string, input: SectionInput) =>
  axios.put<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}/sections/${sectionId}`, input);

export const deleteSection = (moduleId: string, sectionId: string) => axios.delete<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}/sections/${sectionId}`);

// Reorder body matches the controller's ReorderRequestDto: { orderedIds: string[] }.
export const reorderSections = ({ moduleId, orderedIds }: { moduleId: string; orderedIds: string[] }) =>
  axios.post<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}/sections/reorder`, { orderedIds });

// ---- Question mutations -----------------------------------------------------

export const createQuestion = (moduleId: string, input: CreateQuestionInput) => axios.post<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}/questions`, input);

export const updateQuestion = (moduleId: string, questionId: string, input: UpdateQuestionInput) =>
  axios.put<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}/questions/${questionId}`, input);

export const deleteQuestion = (moduleId: string, questionId: string) => axios.delete<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}/questions/${questionId}`);

export const reorderQuestions = ({ moduleId, orderedIds }: { moduleId: string; orderedIds: string[] }) =>
  axios.post<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}/questions/reorder`, { orderedIds });
