import axios from 'axios';
import type { FormSchemaDto } from '../../../types/formSchema';

// Full activity-record form schema (including inactive sections/questions) for the DHSC authoring views.
export const getFormSchema = () => axios.get<FormSchemaDto>('admin/activity-form/schema');

export interface FormCategoryDto {
  id: string;
  name: string;
}

// The activity categories a section can be scoped to (DHSC reference data).
export const getFormCategories = () => axios.get<FormCategoryDto[]>('admin/activity-form/categories');
