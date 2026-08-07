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

// The server refused because the caller is going too fast (BLUM-7239 / CWE-770). Distinct from every helper
// above because it is neither a fault nor a verdict about the record: the same request will succeed shortly.
// Its main job is to keep react-query's default `retry: 3` away from a 429 — retrying a rate limit turns one
// refusal into four requests with backoff, spending the budget that would have let the user back in.
export const isRateLimitedError = (error: unknown): boolean => httpStatusOf(error) === 429;

// How many seconds until the caller may retry, or undefined when the response says nothing usable.
//
// It reads two places, and the second is not redundant. Retry-After is not one of the CORS-safelisted
// response headers, so JavaScript can only see it because SetupCors names it in Access-Control-Expose-Headers
// — the SPA and the API are separate App Services, so every deployed environment is cross-origin. That is a
// server setting one CORS edit away from disappearing, and the failure is silent: the header stays on the
// wire, so curl, the network tab and the backend integration suite all still show it while the browser reads
// nothing. The `retryAfterSeconds` body field costs nothing and cannot be lost that way, so it is the
// fallback. (downloadFile.ts has quietly had this exact problem with content-disposition for a while.)
//
// Only the delta-seconds form is read. RFC 9110 also allows an HTTP-date, but this API never sends one —
// precisely so the number does not depend on the client's clock agreeing with the server's — and guessing at
// a date we do not send would be inventing a number to show a user. Anything zero, negative, fractional or
// unparseable is dropped rather than rounded, so copy built on this never says "try again in 0 seconds".
export const retryAfterOf = (error: unknown): number | undefined => {
  const response = (error as { response?: { headers?: Record<string, unknown>; data?: { retryAfterSeconds?: unknown } } } | undefined)?.response;

  // Axios lower-cases response header names.
  return positiveSeconds(response?.headers?.['retry-after']) ?? positiveSeconds(response?.data?.retryAfterSeconds);
};

const positiveSeconds = (value: unknown): number | undefined => {
  if (typeof value !== 'string' && typeof value !== 'number') {
    return undefined;
  }

  const seconds = Number(value);

  return Number.isInteger(seconds) && seconds > 0 ? seconds : undefined;
};
