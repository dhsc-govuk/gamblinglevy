type DateLike = Date | string | number;

export interface Attributes {
  sub: string;
  given_name: string;
  family_name: string;
  email: string;
  email_verified?: boolean;
  phone_number_verified?: boolean;
  birthdate?: DateLike;
  address?: string;
}

export interface UserInfo {
  id?: string;
  username: string;
  attributes: Attributes;
}

export interface User {
  id: string;
  email: string;
  first_name: string;
  last_name: string;
  full_name: string;
  user_type: string;
  user_role: string;
  status: 'active' | 'deactivated';
  sub_id: string;
  updatedAt: string;
  organisation_id: string;
  location_id: string;
}

export interface AdminUser extends User {}
