import { EnumUserRole, homeLinkForRole } from '../../components/AuthProvider';
import { ROUTES } from '../../helpers/routes';

// Roles allowed to create and edit the quarterly report: every LA role (LA Admin + LA Lead). The view-only
// roles this once excluded were removed in BLUM-7038. Mirrors views/Activities/navigation.ts canManageActivities.
export const canEditReport = (role: EnumUserRole | null): boolean => role === EnumUserRole.ORGANISATION_ADMIN || role === EnumUserRole.LEAD;

// Home / Reports / Quarter N / …extra. The second crumb goes to the Reports landing: it is where a collection is
// chosen, and there is no collection-agnostic report page to return to any more (BLUM-6950). The quarter crumb is
// omitted while the context has not resolved yet — e.g. on the intro page.
export const reportBreadcrumbs = (role: EnumUserRole | null, collectionId: string, quarter?: number | null, extra: { label: string; link: string }[] = []) => [
  { label: 'Home', link: homeLinkForRole(role) },
  { label: 'Reports', link: ROUTES.reports.list },
  ...(quarter != null ? [{ label: `Quarter ${quarter}`, link: ROUTES.reports.tasks(collectionId) }] : []),
  ...extra,
];

// LA "Reports" area (BLUM-6097 / BLUM-6098). The landing is the area home; the previously-submitted list and the
// submission detail nest under it. The current page is the H1, so it is not repeated as a breadcrumb.
export const reportsLandingBreadcrumbs = (role: EnumUserRole | null) => [{ label: 'Home', link: homeLinkForRole(role) }];

export const previousReportsBreadcrumbs = (role: EnumUserRole | null) => [
  { label: 'Home', link: homeLinkForRole(role) },
  { label: 'Reports', link: ROUTES.reports.list },
];

export const submissionDetailBreadcrumbs = (role: EnumUserRole | null) => [
  { label: 'Home', link: homeLinkForRole(role) },
  { label: 'Reports', link: ROUTES.reports.list },
  { label: 'Your reports', link: ROUTES.reports.submitted },
];
