import { EnumUserRole } from '../../components/AuthProvider';

// Roles allowed to manage the users in their local authority — create, edit, deactivate, reactivate,
// delete and reset MFA. LA Lead shares the rest of the LA Admin surface but may only *view* the user list
// (BLUM-7038 AC3), so it is deliberately absent. Mirrors the backend's Role.UserManagers, which gates the
// mutating actions on OrganisationUsersController; this predicate only hides the affordances.
export const canManageUsers = (role: EnumUserRole | null): boolean => role === EnumUserRole.ORGANISATION_ADMIN;
