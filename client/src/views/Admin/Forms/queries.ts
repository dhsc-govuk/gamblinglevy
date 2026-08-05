import axios from 'axios';

// Data-collection forms-builder types (DHSC admin authoring surface). Modules are reusable form
// templates made of sections and questions, authored as drafts and locked once published.
// JSON is camelCase; axios uses relative paths against the configured API baseURL.

export type DcQuestionType = 'Text' | 'Textarea' | 'Number' | 'Radio' | 'Checkbox' | 'Select';

export const DC_QUESTION_TYPES: DcQuestionType[] = ['Text', 'Textarea', 'Number', 'Radio', 'Checkbox', 'Select'];

// Radio / Checkbox / Select require at least one answer option; the free-input types do not.
export const isDcChoiceType = (type: string): boolean => type === 'Radio' || type === 'Checkbox' || type === 'Select';

export type FormModuleStatus = 'draft' | 'published' | 'archived';

export interface DcFormQuestionOptionDto {
  id: string;
  value: string;
  label: string;
  displayOrder: number;
  isActive: boolean;
}

export interface DcFormQuestionDto {
  id: string;
  formModuleId: string;
  sectionId: string | null;
  code: string;
  label: string;
  questionType: DcQuestionType;
  displayOrder: number;
  isRequired: boolean;
  hint: string | null;
  helpText: string | null;
  isActive: boolean;
  options: DcFormQuestionOptionDto[];
}

export interface DcFormSectionDto {
  id: string;
  formModuleId: string;
  sectionNumber: number;
  title: string;
  description: string | null;
  helpText: string | null;
  isActive: boolean;
}

export interface FormModuleDto {
  id: string;
  code: string;
  name: string;
  description: string | null;
  displayOrder: number;
  isActive: boolean;
  status: FormModuleStatus;
}

export interface FormModuleWithChildrenDto extends FormModuleDto {
  sections: DcFormSectionDto[];
  questions: DcFormQuestionDto[];
}

const BASE = 'admin/data-collection-forms';

// All form modules (including drafts and archived) for the DHSC authoring list.
export const getFormModules = () => axios.get<FormModuleDto[]>(`${BASE}/modules`);

// A single module with its sections and questions, for the module editor.
export const getFormModule = (moduleId: string) => axios.get<FormModuleWithChildrenDto>(`${BASE}/modules/${moduleId}`);
