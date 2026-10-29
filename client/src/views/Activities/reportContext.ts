import { useSearchParams } from 'react-router-dom';
import type { QueryClient } from '@tanstack/react-query';
import { EnumUserRole } from '../../components/AuthProvider';
import { ROUTES } from '../../helpers/routes';
import { reportActivitiesKey, reportActivityFormsKey, reportContextKey, reportFundingSpendKey, reportTaskListKey } from '../../helpers/queriesParams';
import { activityBreadcrumbs } from './navigation';
// Imported from these modules directly, never from the views/Reports barrel: the barrel re-exports
// ConfirmActivities, which renders the activities list, which imports this file — that cycle would resolve to
// `undefined` at module init under Vite rather than failing the build. These three are leaves.
import { useReportContextFor } from '../Reports/context';
import { reportBreadcrumbs } from '../Reports/navigation';

interface Crumb {
  label: string;
  link: string;
}

// An activity page is reachable two ways: from the standalone "Activity records" area, or from Section 1 of a
// quarterly return ("Manage your list of activities"). Arriving from Section 1 tags the URL with the id of the
// collection being reported on, so the page keeps the return-flow breadcrumbs and sends the user back into the
// report instead of dropping them on the standalone list (BLUM-6967). The id — rather than a return URL — is what
// lets the trail be rebuilt in full, "Quarter N" crumb and all.
export const REPORT_PARAM = 'report';

// Exported so Section 1 itself labels its own crumb from the same constant: GovUKBreadcrumbs slugs the label into
// the testid, so the two drifting apart would silently split breadcrumbs-item-manage-your-list-of-activities in two.
export const SECTION_ONE_LABEL = 'Manage your list of activities';

// The param is interpolated into a /reports/… path, so a malformed one is rejected up front rather than after the
// context request round-trips — no junk hrefs in the breadcrumbs while the query is in flight.
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// Not part of the hook: the Section 1 list is itself a report page, so it holds its collection id in the route
// params and is handed it as a prop — it has no `?report=` of its own to read.
export const withReportContext = (path: string, collectionId?: string | null): string => {
  if (!collectionId) {
    return path;
  }
  const [base, query = ''] = path.split('?');
  const params = new URLSearchParams(query);
  // set, not append — decorating an already-decorated path (a "Change" link off check-answers already carries
  // ?edit=) must stay idempotent.
  params.set(REPORT_PARAM, collectionId);
  return `${base}?${params.toString()}`;
};

// Invalidated after any activity mutation made from inside the return flow: the task-list statuses, the reportable
// activity list and the funding-spend rows are all server-derived from the activities, so they go stale the moment
// one is added, changed or removed.
export const invalidateReportCaches = (queryClient: QueryClient, collectionId: string): void => {
  if (!collectionId) {
    return;
  }
  queryClient.invalidateQueries({ queryKey: reportContextKey(collectionId) });
  queryClient.invalidateQueries({ queryKey: reportTaskListKey(collectionId) });
  queryClient.invalidateQueries({ queryKey: reportActivitiesKey(collectionId) });
  queryClient.invalidateQueries({ queryKey: reportFundingSpendKey(collectionId) });
  // Every activity's key-results form in this collection: they prefill from the activity record (title, status,
  // answers), so they go stale too. Invalidated by prefix — one call covers them all.
  queryClient.invalidateQueries({ queryKey: reportActivityFormsKey(collectionId) });
};

export const useActivityReportContext = () => {
  const [searchParams] = useSearchParams();
  const rawParam = searchParams.get(REPORT_PARAM) ?? '';
  const paramCollectionId = GUID.test(rawParam) ? rawParam : '';

  // Arriving from Section 1 this key is already populated, so it is a cache hit — the report is read here for the
  // quarter shown in the breadcrumb, and to prove the collection is one this user can actually open.
  const { context } = useReportContextFor(paramCollectionId);

  // We are in the flow only while we hold a real, readable report — not merely "no 404 yet". Every page of the
  // return redirects away when its context read fails, so treating an unresolved or failed context as in-flow would
  // aim `listPath` at a page that bounces the user on arrival: they would save successfully, land on Section 1, be
  // redirected straight out to /reports, and lose the success banner with it. An unreadable context (error, still in
  // flight, foreign collection) therefore degrades to the standalone activity trail, which always renders.
  const inReportFlow = !!context;
  const collectionId = inReportFlow ? paramCollectionId : '';

  return {
    collectionId,
    inReportFlow,
    // Where "done" leads — Section 1 of the return, or the standalone list. Already a /reports/… path in the flow,
    // so it is never itself decorated.
    listPath: inReportFlow ? ROUTES.reports.confirmActivities(collectionId) : ROUTES.activities.list,
    // Carries the context onto the next /activities/… page (list row → activity → change).
    activityLink: (path: string): string => withReportContext(path, collectionId),
    breadcrumbs: (role: EnumUserRole | null, extra: Crumb[] = []) =>
      inReportFlow
        ? reportBreadcrumbs(role, collectionId, context?.quarter, [{ label: SECTION_ONE_LABEL, link: ROUTES.reports.confirmActivities(collectionId) }, ...extra])
        : activityBreadcrumbs(role, extra),
  };
};
