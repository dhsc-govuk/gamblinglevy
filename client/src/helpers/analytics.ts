import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import appConfig from '../config';

// Wrapped defensively: the connection string comes from a CI pipeline variable, not code review —
// a malformed value must degrade to "no telemetry", not crash the app at boot (this runs before
// ReactDOM.render in index.tsx).
const initAppInsights = (): ApplicationInsights | null => {
  if (appConfig.app_insights_connection_string === '') {
    return null;
  }
  try {
    const instance = new ApplicationInsights({
      config: {
        connectionString: appConfig.app_insights_connection_string,
        enableAutoRouteTracking: true,
        enableCorsCorrelation: true,
        enableRequestHeaderTracking: true,
        enableResponseHeaderTracking: true,
        // enableCorsCorrelation injects traceparent/Request-Id headers into every cross-origin
        // request the page makes, not just our own API. MSAL calls these CIAM domains directly
        // (outside our CORS policy), and their token/authorize endpoints don't allow those headers
        // in their CORS preflight response — without this exclusion, sign-in would break.
        correlationHeaderExcludedDomains: ['*.microsoftonline.com', '*.ciamlogin.com', '*.msftauth.net'],
      },
    });
    instance.loadAppInsights();
    return instance;
  } catch {
    return null;
  }
};

const appInsights = initAppInsights();

// Callers fire these inline before a submit/navigate action — a telemetry failure (e.g. the SDK
// throwing on a malformed connection string) must never block the actual user action.
export const trackEvent = (name: string, properties?: Record<string, string>): void => {
  try {
    appInsights?.trackEvent({ name }, properties);
  } catch {
    // Telemetry is best-effort only.
  }
};

export const setAuthenticatedUserContext = (userId: string): void => {
  try {
    appInsights?.setAuthenticatedUserContext(userId);
  } catch {
    // Telemetry is best-effort only.
  }
};
