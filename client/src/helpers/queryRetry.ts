import { onlineManager } from '@tanstack/react-query';
import { httpStatusOf, isNotFoundError, isRateLimitedError } from './httpErrors';

// The request never left the browser: there is no HTTP answer, and the browser says it has no connection.
const failedOffline = (error: unknown): boolean => httpStatusOf(error) === undefined && !onlineManager.isOnline();

// The retry rule every query shares, and the one a query with its own `retry` has to build on: setting `retry`
// replaces the client's default rather than composing with it.
//
// Three retries, as react-query does by default, with two exceptions.
//   - Never for a 429. Retrying a rate limit turns one refusal into four requests with backoff, spending the
//     budget that has to drain before the caller can get back in (BLUM-7239).
//   - Without limit while the browser is offline. react-query v3 paused a retry until the connection came back.
//     v5 can do the same, but a paused query reports isLoading false with no data and no error, which no screen
//     here was written for (see the QueryClient in components/App) — so the client never pauses, and this keeps
//     the query retrying instead. Without it a connection that drops for more than the seven seconds three
//     retries take ends in an error, and the report and maturity pages answer an error by redirecting to the
//     landing page, discarding whatever the user had typed (BLUM-8757).
export const shouldRetryQuery = (failureCount: number, error: unknown): boolean => !isRateLimitedError(error) && (failureCount < 3 || failedOffline(error));

// For a query whose 404 is a verdict (an unknown id, or a feature flag switched off), which a retry cannot change;
// everything else keeps the shared rule.
export const retryUnlessNotFound = (failureCount: number, error: unknown): boolean => !isNotFoundError(error) && shouldRetryQuery(failureCount, error);

// react-query's own backoff, capped at five seconds rather than thirty while offline: nothing listens for the
// connection returning, so the cap is how long a page can go on waiting after it has.
export const queryRetryDelay = (failureCount: number, error: unknown): number => Math.min(1000 * 2 ** failureCount, failedOffline(error) ? 5000 : 30000);
