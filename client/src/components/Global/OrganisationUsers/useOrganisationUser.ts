import { useQuery } from 'react-query';
import { useAuthProvider } from '../../AuthProvider';
import { organisationUserKey } from '../../../helpers/queriesParams';
import { isAccessDeniedError, isNotFoundError, isRateLimitedError } from '../../../helpers/httpErrors';
import { getOrganisationUser, type OrganisationUserDto } from './api';
import { SERVICE_PROBLEM_HEADING } from './UserLoadFailed';

export interface OrganisationUserState {
  user: OrganisationUserDto | undefined;
  // The record is not this organisation's to see. Render the denial page.
  isDenied: boolean;
  // The record could not be read for a reason that is NOT a refusal. Render the problem page.
  loadFailed: boolean;
  // Nothing to show yet, and nothing has gone wrong. Render the loader.
  isLoading: boolean;
}

// The single reader for "the user behind :userId" shared by every /organisation-admin/users/:userId* page.
// It exists because the same twenty lines were copy-pasted into five views and had already drifted — two of them
// guarded the fault branch against cached data and three did not (BLUM-7113 review). Every rule about what a failed
// read means lives here once, so adding a status to the refusal set or a sixth view cannot miss a call site.
export const useOrganisationUser = (userId: string): OrganisationUserState => {
  const { organisationId, isAuthLoading } = useAuthProvider();

  const { data, isError, error } = useQuery(organisationUserKey(organisationId ?? '', userId), () => getOrganisationUser(organisationId!, userId), {
    enabled: !!organisationId && !!userId,
    // A 404 is retry-proof, and a 429 is retry-hostile — retrying a rate limit spends the budget that has to drain
    // before the caller can get back in (BLUM-7239). A 403 keeps its retries on purpose: the axios interceptor
    // deliberately lets an MFA_REQUIRED 403 through inside its 5-second post-verify guard, and the retry backoff is
    // what carries the last attempt past that window so the interceptor can redirect. Failing fast on 403 would
    // strand the user instead.
    retry: (failureCount, requestError) => !isNotFoundError(requestError) && !isRateLimitedError(requestError) && failureCount < 3,
  });

  const user = data?.data;

  // A profile that resolved without an organisation id can never satisfy `enabled`, so the query stays idle and no
  // error ever arrives — reporting that as "loading" is the never-clearing spinner this ticket exists to remove.
  const missingOrganisation = !isAuthLoading && !organisationId;

  // Every verdict is suppressed while a record is in hand: react-query keeps cached data when a background refetch
  // fails, and a good record beats both a denial and a fault banner.
  const isDenied = !user && isError && isAccessDeniedError(error);
  const loadFailed = !user && !isDenied && (missingOrganisation || isError);

  return { user, isDenied, loadFailed, isLoading: !user && !isDenied && !loadFailed };
};

// Page titles must describe the page that is actually rendered, not the one that was requested.
export const organisationUserPageTitle = (state: OrganisationUserState, pageTitle: string): string => {
  if (state.isDenied) return 'Not authorised';
  if (state.loadFailed) return SERVICE_PROBLEM_HEADING;
  return pageTitle;
};
