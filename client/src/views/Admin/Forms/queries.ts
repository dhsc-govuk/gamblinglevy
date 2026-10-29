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

// ---- Versions (BLUM-9102, behind BLUM_9087) ---------------------------------
// Mirror FormModuleVersionSummaryDto / FormModuleVersionDto and the FormModuleDefinition records in
// api/src/Api/Dtos/DataCollectionFormsDtos.cs and Application/DataCollectionForms/Versions/FormModuleDefinition.cs.

export interface FormModuleVersionSummaryDto {
  id: string;
  versionNumber: number;
  publishedAt: string;
  publishedBy: string;
  // Null when the publisher's account no longer exists.
  publishedByName: string | null;
}

export interface FormVersionOptionDto {
  value: string;
  label: string;
  displayOrder: number;
  isActive: boolean;
}

export interface FormVersionSectionDto {
  id: string;
  sectionNumber: number;
  title: string;
  description: string | null;
  helpText: string | null;
  isActive: boolean;
}

export interface FormVersionQuestionDto {
  id: string;
  sectionId: string | null;
  code: string;
  label: string;
  questionType: string;
  displayOrder: number;
  isRequired: boolean;
  hint: string | null;
  helpText: string | null;
  isActive: boolean;
  options: FormVersionOptionDto[];
}

export interface FormModuleVersionDto extends FormModuleVersionSummaryDto {
  code: string;
  name: string;
  description: string | null;
  sections: FormVersionSectionDto[];
  questions: FormVersionQuestionDto[];
}

// A form's saved versions, newest first.
export const getFormModuleVersions = (moduleId: string) => axios.get<FormModuleVersionSummaryDto[]>(`${BASE}/modules/${moduleId}/versions`);

// One saved version with the questions it held.
export const getFormModuleVersion = (moduleId: string, versionNumber: number) => axios.get<FormModuleVersionDto>(`${BASE}/modules/${moduleId}/versions/${versionNumber}`);
