import axios from 'axios';
import { AdminUserDto } from './queries';

export interface CreateUserPayload {
  firstName: string;
  lastName: string;
  email: string;
  isActive: boolean;
}

export interface CreateOrganisationUserPayload {
  firstName: string;
  lastName: string;
  email: string;
  organisationId: string;
  role: string;
  isActive: boolean;
}

export interface UpdateUserPayload {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: string;
  organisationId?: string;
}

export const createUser = (payload: CreateUserPayload) => axios.post<AdminUserDto>('admin/users/create', payload);

export const createOrganisationUser = ({ organisationId, firstName, lastName, email, role, isActive }: CreateOrganisationUserPayload) =>
  axios.post<AdminUserDto>(`organisations/${organisationId}/users/create`, {
    firstName,
    lastName,
    email,
    status: isActive ? 'active' : 'deactivated',
    role,
  });

export const updateUser = (payload: UpdateUserPayload) => axios.put<AdminUserDto>('admin/users/edit', payload);

export const deactivateUser = (userId: string) => axios.post<AdminUserDto>(`admin/users/${userId}/deactivate`);

export const reactivateUser = (userId: string) => axios.post<AdminUserDto>(`admin/users/${userId}/reactivate`);

export const resetUserMfa = (userId: string) => axios.post<AdminUserDto>(`admin/users/${userId}/reset-mfa`);

export const deleteUser = (userId: string) => axios.delete(`admin/users/${userId}`);
