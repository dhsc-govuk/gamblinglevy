import axios from 'axios';
import type { FormSchemaDto } from '../../types/formSchema';

export type ActivityStatus = 'planned' | 'active' | 'closed' | 'finished';

export interface ActivityAnswerDto {
  questionCode: string;
  value: string | null;
}

// An expected/key result row. Standard protocol selections carry the key-result code (with the definition
// label snapshotted into `text` by the backend); custom "Other" free-text rows have a null code.
export interface ExpectedResultDto {
  keyResultCode: string | null;
  text: string;
}

export interface ActivityDto {
  id: string;
  organisationId: string;
  // `title` is the resolved display title (falls back to the type name); `name` is the raw user-entered name.
  title: string;
  name: string | null;
  categoryId: string | null;
  categoryName: string | null;
  taskId: string | null;
  taskName: string | null;
  customTaskText: string | null;
  status: ActivityStatus | null;
  isDraft: boolean;
  startMonth: number | null;
  startYear: number | null;
  // Why a Closed activity was closed (BLUM-7673); null for any other status and for records closed before then.
  closureReason: string | null;
  expectedResults: ExpectedResultDto[];
  answers: ActivityAnswerDto[];
  createdAt: string;
  updatedAt: string | null;
}

export interface ActivitySummaryDto {
  id: string;
  title: string;
  categoryId: string | null;
  categoryName: string | null;
  taskId: string | null;
  taskName: string | null;
  status: ActivityStatus | null;
  isDraft: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface TaxonomyCategoryDto {
  id: string;
  name: string;
}

export interface TaxonomyTaskDto {
  id: string;
  name: string;
  categoryId: string;
}

// A standard key result from the Question Protocol, offered for a specific activity type (task).
export interface TaxonomyKeyResultDto {
  code: string;
  taskId: string;
  label: string;
}

export interface ActivityTaxonomyDto {
  categories: TaxonomyCategoryDto[];
  tasks: TaxonomyTaskDto[];
  keyResults: TaxonomyKeyResultDto[];
}

export const getActivityList = (status?: string) => axios.get<ActivitySummaryDto[]>('activities', { params: status ? { status } : undefined });

export const getDeletedActivities = () => axios.get<ActivitySummaryDto[]>('activities/deleted');

export const getActivity = (activityId: string) => axios.get<ActivityDto>(`activities/${activityId}`);

export const getActivityTaxonomy = () => axios.get<ActivityTaxonomyDto>('activities/taxonomy');

// The active activity-record form schema, scoped to the chosen category (plus any global sections). The
// create/edit flow renders one step per returned section.
export const getActivityFormSchema = (categoryId?: string) => axios.get<FormSchemaDto>('activities/form-schema', { params: categoryId ? { categoryId } : undefined });
