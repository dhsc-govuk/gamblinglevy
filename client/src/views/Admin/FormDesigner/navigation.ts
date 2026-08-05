import { EnumUserRole } from '../../../components/AuthProvider';

// Only DHSC/Departmental admins may manage the activity-record questions (BLUM-6444).
export const canManageFormDesigner = (role: EnumUserRole | null): boolean => role === EnumUserRole.ADMIN;

export const formDesignerBreadcrumbs = (extra: { label: string; link: string }[] = []) => [
  { label: 'Home', link: '/admin/home' },
  { label: 'Activity record questions', link: '/admin/activity-record-questions' },
  ...extra,
];
