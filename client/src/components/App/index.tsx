import { GlobalStyle } from 'govuk-react';
import 'govuk-frontend/dist/govuk/govuk-frontend.min.css';
import '../../styles/baseline.css';
import '../../styles/govuk-refreshed-branding.css';
import { BrowserRouter } from 'react-router-dom';
import { MsalProvider } from '@azure/msal-react';
import { QueryClient, QueryClientProvider, focusManager } from '@tanstack/react-query';
import axios from 'axios';
import type { AccountInfo } from '@azure/msal-browser';
import AuthProvider, { silentRequest, msalConfig, msalInstance, msalInitPromise, isMsalReady } from '../AuthProvider';
import { MfaProvider } from '../MfaProvider';
import AppRoutes from '../../Routes';
import { GovUKNotificationProvider } from '../GovUKComponents';
import {
  canAttemptSessionEnd,
  clearSessionEndAttempts,
  clearSignedOutAccountFor,
  recordSessionEnded,
  recordSignedOutAccount,
  setSessionEndedReason,
  signedOutAccountFor,
  type SessionEndedReason,
} from '../../helpers/sessionEnd';
import { decideAuthFailureAction, sessionEndedReasonFromError, stripBasePath } from '../../helpers/authFailure';
import { queryRetryDelay, shouldRetryQuery } from '../../helpers/queryRetry';
import { isSignInRequiredError, registerExpiredSignInHandler } from '../../helpers/signInRequired';
import appConfig from '../../config.json';

const baseUrl = import.meta.env.BASE_URL || '/';

// react-query must never park a request (BLUM-8757). Since v4 it can leave one "paused" — not fetching, with
// no data and no error — where v3 went on reporting it as loading, and no screen here was written for that
// fourth state: a loading gate falls through to its not-found branch, and the report and maturity pages
// redirect away (`if (!contextLoading && !context) return <Navigate … />`). Two things pause a request, so
// there are two settings:
//   - the browser being offline, which `networkMode: 'always'` below switches off;
//   - a retry coming due while the tab is hidden, in every network mode, which this switches off. Focus has no
//     other job in this app, because refetchOnWindowFocus is false.
// With both, isLoading means what it meant in v3 in every state a query can reach. What v3's pause was for —
// waiting out a dropped connection instead of failing — is done by the retry rule instead: see shouldRetryQuery.
focusManager.setFocused(true);

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      // See the note above.
      networkMode: 'always',
      // 'always' flips this default to false, on the theory that such a query does not need the network. Ours
      // do: without it a query that failed while offline stays failed when the connection returns, and a view
      // with only a loading state sits on its spinner until somebody reloads.
      refetchOnReconnect: true,
      // react-query retries a failed query three times by default, which for a 429 turns one refusal into
      // four requests with backoff — spending the very budget that has to drain before the caller can get
      // back in, and converting a soft limit into a hard one (BLUM-7239). Everything else keeps the default
      // resilience: a single dropped request must not surface as a failed page.
      //
      // A query that sets its own `retry` overrides this rather than composing with it, so each of those has
      // to build on shouldRetryQuery too. Grep for it to find them — enumerating them here would be wrong the
      // first time another query sets a predicate.
      retry: shouldRetryQuery,
      retryDelay: queryRetryDelay,
    },
    mutations: {
      // Same note. A paused mutation never settles either, so the button that started it spins until the
      // connection returns and nothing tells the user their change was not sent.
      networkMode: 'always',
    },
  },
});

(() => {
  if (!appConfig.api_url) {
    throw new Error('API URL is undefined');
  }
})();

axios.defaults.baseURL = appConfig.api_url;
axios.defaults.withCredentials = true;

axios.interceptors.request.use(
  async (config) => {
    if (!isMsalReady()) {
      await msalInitPromise;
    }
    const account = msalInstance.getAllAccounts()[0];
    if (!account) {
      return config;
    }
    try {
      const msalResponse = await msalInstance.acquireTokenSilent({
        ...silentRequest,
        account,
      });
      config.headers['Authorization'] = 'Bearer ' + msalResponse.accessToken;
    } catch (error) {
      // The refresh token is dead or revoked, so nothing this tab sends can carry a bearer again until the user
      // signs in. Sending the request without one, as below, is answered by a bare 401 that no branch of the
      // response interceptor turns into a redirect: the page fills with load errors, and a reload lands on
      // NotAuthorised. End the CIAM sign-in instead, exactly as a SESSION_EXPIRED 401 does, so the user reaches
      // the sign-in page and is told why. This is the only way out of that state since BLUM-8757: silentRequest
      // no longer falls back to the hidden iframe that used to renew the token in some browsers.
      //
      // Only that verdict. A network failure while refreshing says nothing about the sign-in, and signing
      // somebody out because their connection dropped would lose their work for nothing.
      //
      // And never for the sign-out's own request. Someone whose token has died and who then clicks "Sign out"
      // is signing out on purpose: hold mfa/logout here and signOut() never gets past it, so its clean-up is
      // skipped and they are told their session expired. Let it go without a bearer, as it always did — the
      // 401 is caught there, and the CIAM sign-out that follows needs no token.
      if (isSignInRequiredError(error) && !isSignOutRequest(config.url) && endExpiredSignIn()) {
        return new Promise<never>(() => {}); // the sign-out navigates away
      }
      console.error('Failed to acquire token:', error);
    }
    return config;
  },
  (err) => Promise.reject(err)
);

// Guards the sign-out below. A page fires several requests at once, so without this every one of them
// would race to call logoutRedirect and MSAL would throw on the interaction already in progress.
let sessionEndHandled = false;
let sessionEndAttemptsCleared = false;

// Mirrors MfaRequiredMiddleware's exempt prefixes: a 200 from one of these says nothing about whether the
// session survived, because the gate never looked at it.
const isGateExemptRequest = (url?: string): boolean => {
  const path = (url ?? '').replace(/^\/+/, '');
  return path.startsWith('mfa/') || path.startsWith('health') || path.startsWith('swagger');
};

// The explicit sign-out's one API call (signOut in components/AuthProvider).
const isSignOutRequest = (url?: string): boolean => (url ?? '').replace(/^\/+/, '') === 'mfa/logout';

// The app never calls setActiveAccount, so the first cached account is its single source of truth for "who is
// signed in" — the same one AuthProvider derives subId from. Guarded because a broken storage backend must not
// take the interceptor down with it.
const cachedAccount = (): AccountInfo | null => {
  try {
    return (isMsalReady() ? msalInstance.getAllAccounts()[0] : undefined) ?? null;
  } catch {
    return null;
  }
};

// clearCache() cannot report a failure: MSAL catches its own storage errors and only logs them
// (clearCacheOnLogout in BaseInteractionClient), so the promise resolves whether or not the account went. Sweep
// MSAL's keys directly as well, using the predicate MSAL itself uses in BrowserCacheManager.clear — any key
// whose name contains "msal" or the client id. That is what deterministically drops the refresh token, which
// is what makes a silent re-mint possible in the first place.
const purgeMsalSessionStorage = () => {
  try {
    const { clientId } = msalConfig.auth;
    Object.keys(sessionStorage)
      .filter((key) => key.includes('msal') || key.includes(clientId))
      .forEach((key) => sessionStorage.removeItem(key));
  } catch {
    // Nothing further to try here. The signed-out-account marker is the guard that survives this.
  }
};

// The server destroyed the session record (BLUM-7238), so there is nothing left to log out of on the API —
// calling mfa/logout here would only 401 again. What is still live is the CIAM sign-in, and ending that is
// what makes the user re-authenticate rather than just re-enter a code.
const endSignedOutSession = (reason: SessionEndedReason) => {
  sessionEndHandled = true;
  const account = cachedAccount();

  try {
    // The path is dropped rather than kept: after a timeout the user re-authenticates from scratch, and on a
    // shared machine that may not be the same person, so nobody is returned to the previous session's URL.
    sessionStorage.removeItem('mfa_verified_at');
    sessionStorage.removeItem('mfa_redirect_path');
    recordSessionEnded(reason);
  } catch {
    // Blocked or full storage costs the user the explanatory banner, not the sign-out itself. Carry on, so a
    // storage fault can never be the reason a revoked session stays signed in.
  }

  msalInstance
    .logoutRedirect({
      account,
      postLogoutRedirectUri: baseUrl,
    })
    .catch(() =>
      // A failed IdP round trip must not strand the user on a dead page, and dropping MSAL's own cache before
      // navigating is what stops that becoming something worse: since BLUM-7241 the server clears the
      // mfa_session cookie on a SESSION_* 401, so an account left in the cache means the sign-in page fetches
      // the profile again, still authenticated, and is answered 403 MFA_REQUIRED rather than 401 — which on a
      // fresh page load (sessionEndHandled back to false) would send the user to /mfa/verify, where their
      // still-valid token mints a new session and undoes the revocation.
      //
      // Both clears are best-effort and neither can report failure, so they are no longer the last line of
      // defence: the signed-out-account marker recorded by the interceptor is what the 403 branch checks.
      msalInstance
        .clearCache()
        .catch(() => undefined)
        .finally(() => {
          purgeMsalSessionStorage();
          window.location.href = `${baseUrl}sign-in`.replace('//', '/');
        })
    );
};

// A refresh token that can no longer be redeemed: the counterpart of a SESSION_EXPIRED 401 for a sign-in that
// ended at CIAM rather than on our server. Shared by the request interceptor above and by AuthProvider's own
// token call on a page load, which meets the dead token before any request is made. True when a sign-out is
// under way, so the caller should wait for the navigation instead of reporting a failure.
const endExpiredSignIn = (): boolean => {
  if (sessionEndHandled) {
    return true;
  }
  // Recorded before the budget can refuse, for the reason the 401 path does it there: a failed CIAM sign-out
  // leaves the account cached, and the marker is what makes the next "Continue to Microsoft sign in" ask for
  // credentials instead of being answered silently by a session that is still live at the IdP.
  recordSignedOutAccount(cachedAccount()?.localAccountId, 'expired');
  // The budget the 401 path spends: if signing out is not working, say so and let the caller fail, rather than
  // hanging every request on a navigation that is not coming.
  if (!canAttemptSessionEnd()) {
    return false;
  }
  endSignedOutSession('expired');
  return true;
};
registerExpiredSignInHandler(endExpiredSignIn);

// Replaces a Blob error body with the JSON it contains, so the branches below can read data.error. Any
// failure to decode leaves the original Blob in place — a download that genuinely returned binary on error
// should fall through to normal error handling, not blow up the interceptor.
const decodeBlobErrorBody = async (error: { response?: { data?: unknown } }): Promise<void> => {
  const data = error?.response?.data;
  if (!(data instanceof Blob)) {
    return;
  }

  try {
    const text = await data.text();
    error.response!.data = JSON.parse(text);
  } catch {
    // Not JSON, or unreadable — leave the Blob untouched.
  }
};

// Asks the sign-in page for the "you have been signed out" banner. Guarded, unlike the mfa_redirect_path
// writes below, because this one also runs on the reject arm, where an exception would replace the rejection
// the caller is waiting on with an unrelated storage error.
const announceSessionEnded = (reason: SessionEndedReason) => {
  try {
    setSessionEndedReason(reason);
  } catch {
    // Storage blocked. The user still reaches the sign-in page, just without the specific banner.
  }
};

axios.interceptors.response.use(
  (response) => {
    // Only a response the gate actually inspected proves the session is alive. /mfa/* is exempt from the
    // server's gate, so mfa/status answers 200 even for a destroyed session, and counting that would reset
    // the budget on every page load and never bound anything.
    if (isGateExemptRequest(response.config?.url)) {
      return response;
    }

    // Hand the attempt budget back once the session is demonstrably alive again. Guarded by a module flag so
    // this is one storage write per page load, not one per response.
    if (!sessionEndAttemptsCleared) {
      sessionEndAttemptsCleared = true;
      clearSessionEndAttempts();
    }

    // Same proof, but scoped to the account that earned it, and deliberately outside that one-shot. The
    // marker is one shared localStorage key while MSAL's cache is per tab, so two tabs on one origin can hold
    // two different accounts against it: an unscoped clear here let account B's ordinary 200 erase account
    // A's marker with no user action at all. Outside the one-shot because the scoped clear is a no-op for
    // anyone else's marker, and a shared flag would let that first no-op burn it and strand the marker for
    // the rest of the page load.
    clearSignedOutAccountFor(cachedAccount()?.localAccountId);

    return response;
  },
  async (error) => {
    // File downloads use responseType: 'blob', so axios hands back the error body as a Blob and every branch
    // below — which all dispatch on data.error — silently sees undefined. Decoding it first means a timeout
    // or MFA prompt during an export is handled like any other request instead of surfacing as a bare
    // "download failed" on a page that still looks signed in.
    await decodeBlobErrorBody(error);

    const status = error.response?.status;
    const errorCode = error.response?.data?.error;
    const accountId = cachedAccount()?.localAccountId;

    // Recorded here rather than inside endSignedOutSession, and deliberately before the attempt budget can
    // refuse another sign-out: once the budget is spent the 401 is simply rejected, and without the marker the
    // next 403 would send this account to /mfa/verify with the cookie already gone. That is the same fail-open
    // as a failed CIAM sign-out, by a route that needs no MSAL failure at all.
    const endedReason = sessionEndedReasonFromError(status, errorCode);
    if (endedReason !== null) {
      recordSignedOutAccount(accountId, endedReason);
    }

    const mfaVerifiedAt = sessionStorage.getItem('mfa_verified_at');

    const action = decideAuthFailureAction({
      status,
      errorCode,
      currentPath: window.location.pathname,
      basePath: baseUrl,
      sessionEndHandled,
      isRecentlyVerified: mfaVerifiedAt !== null && Date.now() - parseInt(mfaVerifiedAt, 10) < 5000,
      canAttemptSessionEnd: canAttemptSessionEnd(),
      // Only counts when it names the account MSAL still has cached — see signedOutAccountFor.
      forcedSignOutReason: signedOutAccountFor(accountId)?.reason ?? null,
    });

    switch (action.kind) {
      case 'end-session':
        endSignedOutSession(action.reason);
        return new Promise(() => {}); // suspend further handling

      case 'redirect':
        if (action.rememberPath) {
          // Query string included, not just the path: an activity page opened from a quarterly return carries its
          // report context in `?report=` (BLUM-6967), and dropping it here silently ejects the user from the
          // return flow on the far side of re-verifying.
          //
          // Stripped of the base path: MfaVerify restores this via React Router's navigate(), which is
          // basename-aware and re-adds the app's BASE_URL prefix itself. Stashing the raw
          // window.location.pathname (already basename-inclusive under a sub-path deployment) doubled the
          // prefix into a 404 on the far side of verifying.
          sessionStorage.setItem('mfa_redirect_path', `${stripBasePath(baseUrl, window.location.pathname)}${window.location.search}`);
        }
        if (action.forgetPath) {
          sessionStorage.removeItem('mfa_redirect_path');
        }
        if (action.announceReason) {
          // A tab being told about a sign-out it never saw has no reason of its own to show, so give it one.
          announceSessionEnded(action.announceReason);
        }
        window.location.href = action.to;
        return new Promise(() => {}); // suspend further handling

      case 'suspend':
        return new Promise(() => {}); // suspend further handling

      case 'reject':
        if (action.announceReason) {
          // Already on the sign-in page, so there is nowhere to navigate — but the page consumed the reason
          // when it mounted, and on a reload the 403 lands after that. Write it back so the next render says
          // "you have been signed out" rather than falling through to the generic profile-error banner.
          announceSessionEnded(action.announceReason);
        }
        return Promise.reject(error);

      default:
        return Promise.reject(error);
    }
  }
);

const App = () => (
  <MsalProvider instance={msalInstance}>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter basename={baseUrl}>
        <AuthProvider>
          <MfaProvider>
            <GlobalStyle />
            <div className="app-root">
              <GovUKNotificationProvider>
                <AppRoutes />
              </GovUKNotificationProvider>
            </div>
          </MfaProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </MsalProvider>
);

export default App;
