import axios from 'axios';

export interface AdminUserDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  status: 'active' | 'deactivated';
  role: string;
  organisationName?: string;
}

export interface AccountHistoryEntry {
  actorName: string;
  occurredAt: string;
}

export interface AdminUserDetailDto extends AdminUserDto {
  organisationId?: string;
  mfaStatus: 'enabled' | 'not_set_up';
  createdBy?: AccountHistoryEntry;
  lastUpdatedBy?: AccountHistoryEntry;
}

export const getUserList = () => axios.get<AdminUserDto[]>('admin/users');

export const getUser = (userId: string) => axios.get<AdminUserDetailDto>(`admin/users/${userId}`);
