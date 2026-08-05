import { EnumUserRole, homeLinkForRole } from '../../components/AuthProvider';

// Roles allowed to create/edit/delete activities: every LA role (LA Admin + LA Lead). The view-only roles
// this once excluded were removed in BLUM-7038.
export const canManageActivities = (role: EnumUserRole | null): boolean => role === EnumUserRole.ORGANISATION_ADMIN || role === EnumUserRole.LEAD;

export const activityBreadcrumbs = (role: EnumUserRole | null, extra: { label: string; link: string }[] = []) => [
  { label: 'Home', link: homeLinkForRole(role) },
  { label: 'Activity records', link: '/activities' },
  ...extra,
];
