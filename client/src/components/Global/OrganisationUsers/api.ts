import axios from 'axios';

export interface OrgUserActor {
  actorName: string;
  occurredAt: string;
}

export interface OrganisationUserDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  status: 'active' | 'deactivated';
  role: string;
  mfaStatus?: string;
  passwordLoginEnabled?: boolean;
  createdBy?: OrgUserActor;
  lastUpdatedBy?: OrgUserActor;
}

export interface CreateOrganisationUserPayload {
  firstName: string;
  lastName: string;
  email: string;
  role: string;
  status: 'active' | 'deactivated';
}

export interface UpdateOrganisationUserPayload extends CreateOrganisationUserPayload {
  id: string;
}

export const getOrganisationUserList = (organisationId: string) => axios.get<OrganisationUserDto[]>(`organisations/${organisationId}/users`);

export const getOrganisationUser = (organisationId: string, userId: string) => axios.get<OrganisationUserDto>(`organisations/${organisationId}/users/${userId}`);

export const createOrganisationUser = (organisationId: string, payload: CreateOrganisationUserPayload) =>
  axios.post<OrganisationUserDto>(`organisations/${organisationId}/users/create`, payload);

export const updateOrganisationUser = (organisationId: string, payload: UpdateOrganisationUserPayload) =>
  axios.put<OrganisationUserDto>(`organisations/${organisationId}/users/edit`, payload);

export const deactivateOrganisationUser = (organisationId: string, userId: string) => axios.post<OrganisationUserDto>(`organisations/${organisationId}/users/${userId}/deactivate`);

export const reactivateOrganisationUser = (organisationId: string, userId: string) => axios.post<OrganisationUserDto>(`organisations/${organisationId}/users/${userId}/reactivate`);

export const deleteOrganisationUser = (organisationId: string, userId: string) => axios.delete(`organisations/${organisationId}/users/${userId}`);

export const resetOrganisationUserMfa = (organisationId: string, userId: string) => axios.post(`organisations/${organisationId}/users/${userId}/reset-mfa`);

export const enableOrganisationUserPasswordLogin = (organisationId: string, userId: string) =>
  axios.post<OrganisationUserDto>(`organisations/${organisationId}/users/${userId}/enable-password-login`);

export const resetOrganisationUserPassword = (organisationId: string, userId: string) =>
  axios.post<OrganisationUserDto>(`organisations/${organisationId}/users/${userId}/reset-password`);
