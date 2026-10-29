import axios from 'axios';
import { downloadFile } from '../../helpers/downloadFile';
import { encodedJsonBody } from '../../helpers/encodedJsonBody';
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
  closureReason: string | null;
  expectedResults: ExpectedResultInput[];
  answers: ActivityAnswerInput[];
}

export type CreateActivityInput = ActivityFields & { saveAsDraft: boolean };
export type UpdateActivityInput = ActivityFields & { id: string; saveAsDraft: boolean };

export const createActivity = (input: CreateActivityInput) => axios.post<ActivityDto>('activities', ...encodedJsonBody(input));

export const updateActivity = (input: UpdateActivityInput) => {
  const { id, ...body } = input;
  return axios.put<ActivityDto>(`activities/${id}`, ...encodedJsonBody(body));
};

// Soft-delete (the record can be restored from the "Deleted activities" page).
export const deleteActivity = (activityId: string) => axios.delete<ActivityDto>(`activities/${activityId}`);

export const restoreActivity = (activityId: string) => axios.post<ActivityDto>(`activities/${activityId}/restore`);

// "Download activity record" (BLUM-7648): the whole record plus what submitted quarterly reports recorded for it, as a
// CSV. Fetched through axios rather than a plain link because the request needs the MSAL token and mfa_session cookie;
// the server names the file, so the fallback only applies if Content-Disposition is ever lost.
export const downloadActivityRecord = (activityId: string): Promise<void> => downloadFile(`activities/${activityId}/export`, 'activity-record.csv');
