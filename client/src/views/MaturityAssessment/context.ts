import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { defaultStaleTime, maturityContextKey } from '../../helpers/queriesParams';
import { isNotFoundError } from '../../helpers/httpErrors';
import { shouldRetryQuery } from '../../helpers/queryRetry';
import { getMaturityContext } from './queries';

// The maturity assessment for a given collection — the same shape and 404-redirect contract as
// views/Reports/context.ts useReportContextFor, so the two authoring flows behave alike.
export const useMaturityContextFor = (collectionId: string) => {
  const { data, isLoading, isError } = useQuery({
    queryKey: maturityContextKey(collectionId),
    queryFn: () => getMaturityContext(collectionId),
    enabled: !!collectionId,
    // The financial year, deadline and reporting period do not move while someone completes the assessment, and
    // every page of the flow mounts this query. The mutations that can change it invalidate the key explicitly.
    staleTime: defaultStaleTime,
    // A 404 is a verdict (unknown collection, another organisation's, not a maturity window) and a 429 will pass
    // on its own — retrying either is wrong (BLUM-7239); everything else keeps react-query's default resilience.
    retry: (failureCount, requestError) => !isNotFoundError(requestError) && shouldRetryQuery(failureCount, requestError),
  });

  return { context: data?.data, isLoading, isError };
};

export const useMaturityContext = () => {
  const { collectionId = '' } = useParams<{ collectionId: string }>();

  return { collectionId, ...useMaturityContextFor(collectionId) };
};
