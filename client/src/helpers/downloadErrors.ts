import { isRateLimitedError, retryAfterOf } from './httpErrors';

const SERVICE_PROBLEM = 'Sorry, there is a problem preparing your download. Try again.';

// Why a report download failed, in words the user can act on.
//
// Shared by all four screens that download a report — the two DHSC admin ones and the two LA ones — because
// they had the same message as three separate copies of one string constant, and this adds a branch that must
// not be added three times and then drift.
//
// The 429 branch matters more here than anywhere else in the app: exports draw on the tightest budget in the
// service (Export, 10 permits per 300 seconds), because one of them builds a ZIP of per-section CSVs across
// every organisation in a collection. An admin working through a collection's organisations reaches that by
// doing their job, and the generic copy told them to "Try again" — the one thing that cannot work.
//
// Phrased impersonally on purpose. The limiter buckets on the caller's IP address, not on their account
// (RateLimitContext.PartitionKey, which runs before authentication), so two admins behind one departmental
// egress share this budget. "You have downloaded too many files" would be flatly untrue for the person
// reading it, who may have downloaded one.
//
// Note that the number comes from the Retry-After header alone on this path. These requests use
// responseType: 'blob', so axios leaves an error body as a Blob and retryAfterOf's `retryAfterSeconds`
// fallback cannot read it — the header, and therefore SetupCors continuing to expose it, is the only source.
// The undefined branch below is the real degradation path, not a formality.
export const downloadErrorMessage = (error: unknown): string => {
  if (!isRateLimitedError(error)) {
    return SERVICE_PROBLEM;
  }

  const seconds = retryAfterOf(error);

  if (seconds === undefined) {
    return 'Too many files have been downloaded in a short time. Try again in a few minutes.';
  }

  // Minutes rather than a second count, matching the rounding in views/Mfa/messages.ts: a number the page
  // cannot count down is stale as soon as it renders, and the Export window is five minutes, so seconds imply
  // a precision the figure does not have. positiveSeconds only yields positive integers, so this is never 0.
  const minutes = Math.ceil(seconds / 60);

  return minutes <= 1 ? 'Too many files have been downloaded. Try again in 1 minute.' : `Too many files have been downloaded. Try again in ${minutes} minutes.`;
};
