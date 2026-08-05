import { adminRoles, type EnumUserRole } from '../../../components/AuthProvider';

// Only DHSC/Departmental admins may manage data-collection form modules (the forms builder).
export const canManageForms = (role: EnumUserRole | null): boolean => role !== null && adminRoles.includes(role);

export const formsBuilderBreadcrumbs = (extra: { label: string; link: string }[] = []) => [
  { label: 'Home', link: '/admin/home' },
  { label: 'Forms', link: '/admin/forms' },
  ...extra,
];
