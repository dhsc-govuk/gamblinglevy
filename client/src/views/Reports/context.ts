import { useParams } from 'react-router-dom';
import { useQuery } from 'react-query';
import { defaultStaleTime, reportContextKey } from '../../helpers/queriesParams';
import { getReportContext } from './queries';

// Same untyped-error read as mapReportError — axios errors reach react-query as `unknown`.
const statusOf = (error: unknown): number | undefined => (error as { response?: { status?: number } } | undefined)?.response?.status;

const isNotFound = (error: unknown): boolean => statusOf(error) === 404;

// The report for a given collection. Split out from useReportContext so a page that carries its collection id
// somewhere other than the route params can read the same report — the activity pages entered from the return flow
// hold theirs in a `?report=` query param (BLUM-6967) and must hit this exact cache entry rather than open a
// second one.
export const useReportContextFor = (collectionId: string) => {
  const { data, isLoading, isError } = useQuery(reportContextKey(collectionId), () => getReportContext(collectionId), {
    enabled: !!collectionId,
    // The quarter, deadline and reporting period do not move while someone authors a report, and every page of the
    // flow mounts this query — without a stale time each step re-fetched it. The mutations that can change it
    // invalidate the key explicitly.
    staleTime: defaultStaleTime,
    // A 404 is a verdict, not a blip — the collection is unknown, belongs to another organisation, or the URL is a
    // stale link from the old unparameterised flow (/reports/quarterly/tasks). Fail fast into the caller's redirect
    // rather than retrying three times. Every other failure keeps react-query's default resilience: a single dropped
    // request must not bounce someone out of the report they are part-way through authoring.
    retry: (failureCount, requestError) => !isNotFound(requestError) && failureCount < 3,
  });

  // isError drives the callers' redirect back to the Reports landing: either the 404 above, or a request that kept
  // failing through its retries — in both cases there is no collection to author against on this page.
  return { context: data?.data, isLoading, isError };
};

// The quarterly report the user opened, resolved from the collection id in the URL (BLUM-6950). Every authoring
// page reads its collection this way — never from a server-side "the organisation's current window" pick, which
// silently opened the earliest-deadline window whenever two quarterly windows were open at once.
export const useReportContext = () => {
  const { collectionId = '' } = useParams<{ collectionId: string }>();

  return { collectionId, ...useReportContextFor(collectionId) };
};
