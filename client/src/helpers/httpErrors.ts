// Axios errors reach react-query typed as `unknown`, so read the status defensively rather than casting to AxiosError.
export const httpStatusOf = (error: unknown): number | undefined => (error as { response?: { status?: number } } | undefined)?.response?.status;

// The verdict "this record is not yours to see". Organisation-scoped endpoints answer 404 for a record outside the
// caller's own LA — deliberately indistinguishable from one that does not exist, so probing cannot confirm another
// organisation's ids (BLUM-7113) — and the role gate answers 403. Every other status is a fault, not a refusal:
// telling someone they lack permission when the API is simply down is both untrue and a support ticket.
export const isAccessDeniedError = (error: unknown): boolean => {
  const status = httpStatusOf(error);
  return status === 403 || status === 404;
};

// The subset of the above that no amount of retrying will change. A 403 is excluded deliberately — see the retry
// predicate in useOrganisationUser for why an MFA 403 must keep its retries.
export const isNotFoundError = (error: unknown): boolean => httpStatusOf(error) === 404;

// The role gate's refusal on its own, kept separate from isAccessDeniedError above. That helper folds 403 and 404
// together because an organisation-scoped read cannot tell them apart without leaking another LA's ids. A surface
// that is NOT organisation-scoped — the DHSC Reports area sees every organisation — can be honest about which one
// happened, so it splits them instead of calling a missing report a permission problem.
export const isForbiddenError = (error: unknown): boolean => httpStatusOf(error) === 403;
