import { EnumUserRole, homeLinkForRole } from '../../../components/AuthProvider';

// Breadcrumbs for the DHSC submission-visibility screens. The Reports list shows just "Home"; sub-pages add
// "Reports" (and, for the detail, the reporting window).
export const reportsListBreadcrumbs = () => [{ label: 'Home', link: homeLinkForRole(EnumUserRole.ADMIN) }];

export const reportBreadcrumbs = (extra: { label: string; link: string }[] = []) => [...reportsListBreadcrumbs(), { label: 'Reports', link: '/admin/reports' }, ...extra];
