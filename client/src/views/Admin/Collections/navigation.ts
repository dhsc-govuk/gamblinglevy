import { EnumUserRole, adminRoles, homeLinkForRole } from '../../../components/AuthProvider';

// Only Departmental Admins manage data collections.
export const canManageCollections = (role: EnumUserRole | null): boolean => role !== null && adminRoles.includes(role);

export const collectionBreadcrumbs = (extra: { label: string; link: string }[] = []) => [
  { label: 'Home', link: homeLinkForRole(EnumUserRole.ADMIN) },
  { label: 'Management', link: '/admin/collections' },
  ...extra,
];
