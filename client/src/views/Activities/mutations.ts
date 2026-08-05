import axios from 'axios';
import { ActivityDto } from './queries';

export interface ActivityAnswerInput {
  questionCode: string;
  value: string;
}

// A key-result selection: standard protocol rows send the code (text may be empty — the backend snapshots
// the definition label); custom "Other" rows send a null code with the free text.
export interface ExpectedResultInput {
  keyResultCode: string | null;
  text: string;
}

export interface ActivityFields {
  title: string | null;
  categoryId: string | null;
  taskId: string | null;
  customTaskText: string | null;
  status: string | null;
  startMonth: number | null;
  startYear: number | null;
  expectedResults: ExpectedResultInput[];
  answers: ActivityAnswerInput[];
}

export type CreateActivityInput = ActivityFields & { saveAsDraft: boolean };
export type UpdateActivityInput = ActivityFields & { id: string; saveAsDraft: boolean };

export const createActivity = (input: CreateActivityInput) => axios.post<ActivityDto>('activities', input);

export const updateActivity = (input: UpdateActivityInput) => {
  const { id, ...body } = input;
  return axios.put<ActivityDto>(`activities/${id}`, body);
};

// Soft-delete (the record can be restored from the "Deleted activities" page).
export const deleteActivity = (activityId: string) => axios.delete<ActivityDto>(`activities/${activityId}`);

export const restoreActivity = (activityId: string) => axios.post<ActivityDto>(`activities/${activityId}/restore`);
