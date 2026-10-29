import axios from 'axios';
import { LocalAuthorityDto } from './queries';

export interface CreateLocalAuthorityPayload {
  name: string;
  onsCode: string | null;
  region: string;
  type: 'LA' | 'VCSE';
  eCode: string;
  allocatedAmount: number;
  // The financial year allocatedAmount belongs to, and the upcoming year's allocation (BLUM-7608). The
  // upcoming year itself is never sent: the API always derives it as the year after financialYear.
  financialYear: string;
  nextYearAllocatedAmount: number | null;
  // "When do you want to activate this account?" (BLUM-7945). Required: null (not answered) is rejected by the API
  // with a 400 rather than read as either option.
  activateImmediately: boolean | null;
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

export type BulkImportAdminUserOutcome = 'NotProvided' | 'NotAttempted' | 'Created' | 'AlreadyExists' | 'Failed';

export interface BulkImportRowOutcomeDto {
  rowNumber: number;
  onsCode: string | null;
  name: string | null;
  outcome: 'Created' | 'Updated' | 'Skipped' | 'Failed';
  reason: string | null;
  // Null on the bulk-allocations response, which shares this DTO and creates no users.
  adminUserOutcome: BulkImportAdminUserOutcome | null;
  adminUserEmail: string | null;
  adminUserReason: string | null;
}

export interface BulkImportResultDto {
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
  failedCount: number;
  rows: BulkImportRowOutcomeDto[];
  adminUsersCreatedCount: number;
  adminUsersFailedCount: number;
  adminUsersNotAttemptedCount: number;
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

export interface AmendLocalAuthorityAllocationPayload {
  allocatedAmount: number;
  financialYear: string;
  nextYearAllocatedAmount: number | null;
}

export const amendLocalAuthorityAllocation = (organisationId: string, payload: AmendLocalAuthorityAllocationPayload) =>
  axios.patch<LocalAuthorityDto>(`organisations/local-authorities/${organisationId}/allocation`, payload);

export const bulkAmendAllocations = (file: File) => {
  const form = new FormData();
  form.append('file', file);
  return axios.post<BulkImportResultDto>('organisations/local-authorities/bulk-allocations', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
};

export const downloadBulkAmendAllocationsTemplate = () => axios.get<Blob>('organisations/local-authorities/bulk-allocations/template', { responseType: 'blob' });
