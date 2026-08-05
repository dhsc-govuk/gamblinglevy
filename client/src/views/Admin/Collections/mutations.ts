import axios from 'axios';
import { CollectionDto, CollectionType, ReminderOffsetType } from './queries';

export interface CollectionReminderInput {
  offsetType: ReminderOffsetType;
  days: number;
}

// Mirrors CreateCollectionRequestDto. Note the create-specific date field names: `openDate` / `deadline`.
// Everything except name/saveAsDraft is optional/nullable — a draft may leave any question unanswered.
export interface CreateCollectionInput {
  name: string;
  purpose?: string;
  type?: CollectionType;
  financialYear?: string;
  quarter?: number | null;
  windowSameAsReportingPeriod: boolean | null;
  openDate?: string;
  deadline?: string;
  isSubmittedByAllLas: boolean | null;
  saveAsDraft: boolean;
  localAuthorityIds?: string[];
  formModuleIds?: string[];
  reminders?: CollectionReminderInput[];
}

// Mirrors UpdateCollectionRequestDto. Note the update-specific date field names:
// `collectionOpenDate` / `submissionDeadline` (differs from the create request).
export interface UpdateCollectionInput {
  id: string;
  name?: string;
  purpose?: string;
  type?: CollectionType;
  financialYear?: string;
  quarter?: number | null;
  windowSameAsReportingPeriod: boolean | null;
  collectionOpenDate?: string;
  submissionDeadline?: string;
  isSubmittedByAllLas: boolean | null;
  saveAsDraft: boolean;
  formModuleIds?: string[];
  localAuthorityIds?: string[];
  reminders?: CollectionReminderInput[];
}

export const createCollection = (input: CreateCollectionInput) => axios.post<CollectionDto>('data-collections', input);

export const updateCollection = (input: UpdateCollectionInput) => {
  const { id, ...body } = input;
  return axios.put<CollectionDto>(`data-collections/${id}`, body);
};

// Soft-delete (the record can be restored from the "Deleted collections" page).
export const deleteCollection = (id: string) => axios.delete<CollectionDto>(`data-collections/${id}`);

export const restoreCollection = (id: string) => axios.post<CollectionDto>(`data-collections/${id}/restore`);

export const sendManualReminder = (id: string) => axios.post<CollectionDto>(`data-collections/${id}/send-reminder`);

export const revertToDraft = (id: string) => axios.post<CollectionDto>(`data-collections/${id}/revert-to-draft`);

export const closeCollection = (id: string) => axios.post<CollectionDto>(`data-collections/${id}/close`);
