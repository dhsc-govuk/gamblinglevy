import { useAuthProvider } from '../components/AuthProvider';
import { FeatureFlag, isFeatureEnabled } from '../helpers/featureFlags';

// Whether a feature flag is switched on in this environment, for hiding an unreleased link, button or section. The
// API gates the endpoint behind it as well ([RequiresFeature] answers 404), so this is presentation, not access
// control. Reads false until the profile has loaded. The pre-sign-in, MFA and public policy pages never fetch the
// profile, so there it is whatever the last load in this tab said, or false — unreliable, so don't gate anything on
// those pages. The list is fetched once per page load and nothing refreshes it, so a flag switched off mid-session
// stays on here until a reload: the view behind it must handle the API's 404 (render NotFound) rather than show a
// generic error. For a whole route, use FeatureRoute instead.
const useFeature = (flag: FeatureFlag): boolean => isFeatureEnabled(useAuthProvider().features, flag);

export default useFeature;
