import axios from 'axios';
import { LocalAuthorityDto } from './queries';

export interface CreateLocalAuthorityPayload {
  name: string;
  onsCode: string | null;
  region: string;
  type: 'LA' | 'VCSE';
  eCode: string;
  allocatedAmount: number;
}

export interface UpdateLocalAuthorityPayload {
  name: string;
  onsCode: string | null;
  region: string;
  type: 'LA' | 'VCSE';
  eCode: string;
  onboardingStatus?: null;
  confirmOnsCodeChange: boolean;
}

export interface BulkImportRowOutcomeDto {
  rowNumber: number;
  onsCode: string | null;
  name: string | null;
  outcome: 'Created' | 'Updated' | 'Skipped' | 'Failed';
  reason: string | null;
}

export interface BulkImportResultDto {
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
  failedCount: number;
  rows: BulkImportRowOutcomeDto[];
}

export const createLocalAuthority = (payload: CreateLocalAuthorityPayload) => axios.post<LocalAuthorityDto>('organisations/local-authorities', payload);

export const updateLocalAuthority = (organisationId: string, payload: UpdateLocalAuthorityPayload) =>
  axios.put<LocalAuthorityDto>(`organisations/local-authorities/${organisationId}`, payload);

export const deleteLocalAuthority = (organisationId: string, acknowledgeActiveUsers = false) =>
  axios.delete(`organisations/local-authorities/${organisationId}`, {
    params: acknowledgeActiveUsers ? { acknowledgeActiveUsers: true } : undefined,
  });

export const suspendLocalAuthority = (organisationId: string) => axios.post<LocalAuthorityDto>(`organisations/local-authorities/${organisationId}/suspend`);

export const reinstateLocalAuthority = (organisationId: string) => axios.post<LocalAuthorityDto>(`organisations/local-authorities/${organisationId}/reinstate`);

export const bulkImportLocalAuthorities = (file: File) => {
  const form = new FormData();
  form.append('file', file);
  return axios.post<BulkImportResultDto>('organisations/local-authorities/bulk-import', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
};

export const downloadBulkImportTemplate = () => axios.get<Blob>('organisations/local-authorities/bulk-import/template', { responseType: 'blob' });

export const amendLocalAuthorityAllocation = (organisationId: string, allocatedAmount: number) =>
  axios.patch<LocalAuthorityDto>(`organisations/local-authorities/${organisationId}/allocation`, { allocatedAmount });

export const bulkAmendAllocations = (file: File) => {
  const form = new FormData();
  form.append('file', file);
  return axios.post<BulkImportResultDto>('organisations/local-authorities/bulk-allocations', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
};

export const downloadBulkAmendAllocationsTemplate = () => axios.get<Blob>('organisations/local-authorities/bulk-allocations/template', { responseType: 'blob' });
