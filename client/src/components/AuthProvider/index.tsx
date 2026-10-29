import React, { useContext, useEffect, useState } from 'react';
import { useMsal } from '@azure/msal-react';
import { CacheLookupPolicy, LogLevel, PromptValue, PublicClientApplication, type Configuration } from '@azure/msal-browser';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import * as Sentry from '@sentry/react';
import { User } from '../../types';
import { getProfile } from './queries';
import { mfaLogout } from '../MfaProvider/queries';
import appConfig from '../../config';
import { defaultStaleTime, profileDateCacheKey } from '../../helpers/queriesParams';
import { ROUTES } from '../../helpers/routes';
import { stringFromArray } from '../../helpers/stringUtils';
import { setAuthenticatedUserContext } from '../../helpers/analytics';
import { clearSessionEndedReason, clearSignedOutAccount, readSignedOutAccount, reassertSignedOutAccount, releaseSignedOutAccount } from '../../helpers/sessionEnd';
import { endSignInThatExpired, isSignInRequiredError } from '../../helpers/signInRequired';

(() => {
  if (!appConfig.ad_tenant_id) {
    throw new Error('Tenant ID is undefined');
  }
  if (!appConfig.ad_client_id) {
    throw new Error('Client Id is undefined');
  }
  if (!appConfig.ad_authority) {
    throw new Error('Authority is undefined');
  }
  if (!appConfig.ad_redirect_uri) {
    throw new Error('Redirect URI is undefined');
  }
  if (!appConfig.ad_scope) {
    throw new Error('Scope is undefined');
  }
})();

const config = {
  tenantId: appConfig.ad_tenant_id,
  clientId: appConfig.ad_client_id,
  authority: appConfig.ad_authority,
  redirectUri: appConfig.ad_redirect_uri,
  scopes: [appConfig.ad_scope],
};

// `satisfies`, so an option MSAL has since removed fails the build instead of being passed and silently
// ignored — an untyped literal let navigateToLoginRequestUrl and storeAuthStateInCookie outlive their removal
// in v5 (BLUM-8757). Not an annotation, which would make `auth` optional for the code that reads clientId.
export const msalConfig = {
  auth: {
    clientId: config.clientId,
    authority: config.authority,
    // The second host is not optional. CIAM's discovery document names its `issuer` by the tenant's id
    // (https://{tenant id}.ciamlogin.com/…) even though the authority uses the tenant's name, and MSAL v5
    // validates that issuer: a host other than the authority's is accepted only when it is listed here.
    // Without it every sign-in fails with endpoints_resolution_error before the redirect starts (BLUM-8757).
    // It is also the first thing ad_tenant_id is used for, so a wrong value there now breaks sign-in.
    knownAuthorities: [new URL(config.authority).hostname, `${config.tenantId}.ciamlogin.com`],
    redirectUri: config.redirectUri,
    postLogoutRedirectUri: import.meta.env.BASE_URL || '/',
  },
  cache: {
    cacheLocation: 'sessionStorage',
  },
  system: {
    loggerOptions: {
      loggerCallback: (level: LogLevel, message: string, containsPii: boolean) => {
        if (containsPii) {
          return;
        }
        switch (level) {
          case LogLevel.Error:
            console.error(message);
            return;
          default:
            return;
        }
      },
    },
  },
} satisfies Configuration;

export const loginRequest = {
  scopes: config.scopes,
  // Skips the email-based Home Realm Discovery step on the sign-in page (see the domain_hint parameter at
  // https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow) — without it, a local
  // account whose email domain is independently verified/federated in another real Entra tenant (e.g. a local
  // authority's own Microsoft 365 tenant) gets redirected to that tenant's sign-in page instead of this one's
  // local-account/email-OTP flow (BLUM-7848). Empty when ad_domain_hint isn't configured for an environment.
  ...(appConfig.ad_domain_hint ? { extraQueryParameters: { domain_hint: appConfig.ad_domain_hint } } : {}),
};

// Every silent token call uses this, never loginRequest on its own (BLUM-8757). MSAL's default policy falls back
// to a hidden iframe once the refresh token is dead, and from v5 that iframe can only answer through a redirect
// bridge page (BroadcastChannel), which this app does not serve — so the fallback would hold every request for
// the 10 s iframeBridgeTimeout and then fail anyway. This policy stops at the refresh token and fails at once
// with InteractionRequiredAuthError, which the request interceptor in components/App turns into a sign-out.
//
// Nothing is lost by it. The refresh token lasts 24 h and the MFA session 8 h, so a renewal the iframe could
// have rescued was about to be answered SESSION_EXPIRED regardless, and the iframe already failed wherever
// third-party cookies are blocked. Going back to the default policy needs the bridge page, and its redirect
// URI registered in Entra, first.
export const silentRequest = { ...loginRequest, cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken };

// MSAL requires explicit initialize() + handleRedirectPromise() before any
// token acquisition. Centralising both here lets AuthProvider gate its loading
// signal on init completion (otherwise the auth guard renders 403 during the
// brief window before MSAL has parsed sessionStorage / the redirect hash).
export const msalInstance = new PublicClientApplication(msalConfig);

let msalReadyResolved = false;
export const msalInitPromise = msalInstance
  .initialize()
  // navigateToLoginRequestUrl is an argument here since v5, not a config option. False processes the response
  // on the redirect URI, where Splash routes by role, rather than bouncing through the page that began sign-in.
  .then(() => msalInstance.handleRedirectPromise({ navigateToLoginRequestUrl: false }))
  .then(() => {
    msalReadyResolved = true;
  })
  .catch((error) => {
    console.error('MSAL initialization error:', error);
    msalReadyResolved = true; // unblock requests even if init failed
  });

export const isMsalReady = () => msalReadyResolved;

// The MVP role set (BLUM-7038). Mirrors Domain/ValueObjects/UserRole.cs — the backend rejects anything else.
export enum EnumUserRole {
  ADMIN = 'admin',
  ORGANISATION_ADMIN = 'organisation admin',
  LEAD = 'lead',
}

export const ROLE_LABELS: Record<string, string> = {
  [EnumUserRole.ADMIN]: 'Super Admin',
  [EnumUserRole.ORGANISATION_ADMIN]: 'LA Admin',
  [EnumUserRole.LEAD]: 'LA Lead',
};

// Labels for roles retired in BLUM-7038 that can still appear in immutable audit-log snapshots — the
// migration collapses live users.role values to 'lead' but deliberately never rewrites audit history.
// Kept as a superset of ROLE_LABELS and used ONLY by the deleted-user history views, so a retired role
// can never leak back into an assignable-role dropdown (those iterate laAdminAssignableRoles, never this).
export const HISTORICAL_ROLE_LABELS: Record<string, string> = {
  ...ROLE_LABELS,
  'location admin': 'Location Admin',
  contributor: 'LA contributor',
  finance: 'Finance',
  'read only': 'Read only',
};

// Roles an LA admin may assign. Feeds every role dropdown, so it is the single place to change them.
export const laAdminAssignableRoles: EnumUserRole[] = [EnumUserRole.ORGANISATION_ADMIN, EnumUserRole.LEAD];

export const roleToAreaMap = (role: EnumUserRole) => {
  switch (role) {
    case EnumUserRole.ADMIN:
      return 'admin';
    case EnumUserRole.ORGANISATION_ADMIN:
    case EnumUserRole.LEAD:
      return 'organisation-admin';
    default:
      return 'unknown';
  }
};

export const adminRoles = [EnumUserRole.ADMIN];
export const organisationAdminRoles = [EnumUserRole.ORGANISATION_ADMIN];

// Every LA role. Mirrors the backend's Role.AnyOrganisationMember — these are the roles that carry an
// organisationId and reach the LA surface (home, activities, reports, funding, read-only user list).
export const laRoles: EnumUserRole[] = [EnumUserRole.ORGANISATION_ADMIN, EnumUserRole.LEAD];

// "Home" resolves to the signed-in user's own dashboard.
export const homeLinkForRole = (role: EnumUserRole | null): string => {
  switch (role) {
    case EnumUserRole.ADMIN:
      return ROUTES.admin.home;
    case EnumUserRole.ORGANISATION_ADMIN:
    case EnumUserRole.LEAD:
      return ROUTES.organisationAdmin.home;
    default:
      return ROUTES.root;
  }
};

export interface HeaderNavItem {
  label: string;
  href: string;
  // Extra path prefixes that should keep this tab highlighted. Defaults to [href] when omitted.
  // Used for grouped tabs like "Management" that span several sub-routes (users, organisations, etc.).
  activePrefixes?: string[];
}

// Header service-navigation items per role. Links must resolve to routes the role can actually
// reach (see Routes.tsx) — otherwise non-admin users hit the 404 page.
export const navigationForRole = (role: EnumUserRole | null): HeaderNavItem[] => {
  switch (role) {
    case EnumUserRole.ADMIN:
      return [
        { label: 'Reports', href: ROUTES.admin.reports.list },
        // Trends has no route or view yet, so the tab led straight to the 404 page (BLUM-7820).
        // Restore this entry alongside the /admin/trends route when the page is built.
        {
          label: 'Management',
          href: ROUTES.admin.management,
          activePrefixes: [ROUTES.admin.collections.list, ROUTES.admin.users.list, ROUTES.admin.organisations.list, ROUTES.admin.auditLogs.list],
        },
        // Guidance is a top-level tab for DHSC too (BLUM-7819), matching the LA nav's last position. It is
        // deliberately NOT in Management's activePrefixes any more: isActive() prefix-matches, so leaving it
        // there would light up both tabs on /admin/guidance.
        { label: 'Guidance', href: ROUTES.admin.guidance },
      ];
    // Lead shares the LA Admin navigation: it reaches every LA surface, and the user-management actions
    // behind "Manage account" are hidden from it per-page via canManageUsers (BLUM-7038).
    case EnumUserRole.ORGANISATION_ADMIN:
    case EnumUserRole.LEAD:
      return [
        { label: 'Reports', href: ROUTES.reports.list, activePrefixes: [ROUTES.reports.list, ROUTES.activities.list] },
        {
          label: 'Manage account',
          href: ROUTES.organisationAdmin.home,
          activePrefixes: [ROUTES.organisationAdmin.users.list],
        },
        { label: 'Guidance', href: ROUTES.organisationAdmin.guidance },
      ];
    default:
      return role ? [{ label: 'Home', href: homeLinkForRole(role) }] : [];
  }
};

interface AuthContextItems {
  userId: string | null;
  user: User | null;
  userRole: EnumUserRole | null;
  organisationId: string | null;
  organisationName: string | null;
  locationId: string | null;
  // True while MSAL is resolving its session OR the profile query is in flight.
  // Lets gating components (e.g. ProtectedRoleRoute) show a loading screen instead
  // of mis-classifying a not-yet-loaded user as unauthorised.
  isAuthLoading: boolean;
  // The profile fetch came back and failed. Distinguishes "we do not have a role yet" from "we are never
  // going to get one", which the absence of userRole on its own cannot: the query is retry: false, so a
  // screen that waits for a role it will never receive waits forever.
  profileErrored: boolean;
  // Feature flags switched on in this environment, from the profile — empty until it loads. Read through
  // useFeature(), or pass it to FeatureRoute. Taken straight from the profile query rather than from state, so it
  // is not cleared on sign-out or a profile error like the fields above: harmless while flags are environment-wide
  // (every user gets the same list), but it would need clearing if flags ever became per-user or per-organisation.
  features: readonly string[];
  // Settles only when the redirect did not happen, and then it rejects.
  signIn: () => Promise<void>;
  signOut: () => Promise<void>;
}

export const AuthContext = React.createContext<AuthContextItems | null>(null);

// One shared empty list, so a context consumer comparing `features` between renders sees no change before the
// profile loads.
const NO_FEATURES: readonly string[] = [];

type Props = { children: React.ReactNode };

const AuthProvider = ({ children }: Props): React.ReactElement => {
  const { instance, accounts } = useMsal();
  const [subId, setSubId] = useState<string | null>(null);
  const [user, setUser] = useState<User | null>(null);
  const [userRole, setUserRole] = useState<EnumUserRole | null>(null);
  const [organisationName, setOrganisationName] = useState<string | null>(null);
  const [authBootstrapFailed, setAuthBootstrapFailed] = useState(false);
  // The cached sign-in turned out to be dead and is being ended. Never reset: a navigation follows either way.
  const [endingExpiredSignIn, setEndingExpiredSignIn] = useState(false);
  // Flips to true once msalInstance.initialize() + handleRedirectPromise()
  // resolve. Until then, MSAL's account list is empty and useIsAuthenticated()
  // returns false, so we must treat the app as still loading to avoid
  // rendering a 403 fallback during MSAL bootstrap.
  const [msalReady, setMsalReady] = useState<boolean>(isMsalReady);

  useEffect(() => {
    if (msalReady) return;
    let cancelled = false;
    msalInitPromise.then(() => {
      if (!cancelled) setMsalReady(true);
    });
    return () => {
      cancelled = true;
    };
  }, [msalReady]);

  const location = useLocation();
  const navigate = useNavigate();
  const isOnMfaPage = location.pathname.startsWith('/mfa/');
  // The public policy pages — cookies (BLUM-6672) and the accessibility statement (BLUM-7684) — are
  // documents, not app surfaces: readable signed out, and linked from the footer of *every* page,
  // including /mfa/verify. They are therefore treated exactly like /mfa/* below — profile fetch skipped,
  // and excluded from the auth-loading gate that fetch would otherwise resolve.
  //
  // Without this a signed-in-but-unverified reader who clicks the footer link is answered 403 MFA_REQUIRED,
  // bounced straight back to /mfa/verify, and — the part that actually loses their work —
  // decideAuthFailureAction returns rememberPath for any path but / and /sign-in, so the interceptor
  // overwrites the mfa_redirect_path stashed for the page they were originally trying to reach.
  const isOnPublicPolicyPage = location.pathname === ROUTES.public.cookies || location.pathname === ROUTES.public.accessibilityStatement;
  const msalAccount = accounts[0] ?? (msalReady ? instance.getAllAccounts()[0] : undefined);
  const hasMsalAccount = Boolean(msalAccount);
  const {
    data: profileData,
    isSuccess: profileLoaded,
    isError: profileErrored,
  } = useQuery({
    queryKey: profileDateCacheKey,
    queryFn: getProfile,
    enabled: !!subId && !isOnMfaPage && !isOnPublicPolicyPage,
    staleTime: defaultStaleTime,
    retry: false,
  });

  useEffect(() => {
    const signIn = async () => {
      if (msalAccount) {
        const tokenRequest = { ...silentRequest, account: msalAccount };
        const response = await instance.acquireTokenSilent(tokenRequest);
        setAuthBootstrapFailed(false);
        setSubId(response.account?.localAccountId ?? '');
      }
    };
    if (hasMsalAccount && !subId) {
      signIn().catch((error) => {
        console.error(error);
        // A refresh token that can no longer be redeemed is not "this user has no role". End the sign-in here,
        // as the request interceptor does on a request, rather than fall through to the unauthorised page and
        // leave the rescue to whichever provider happens to make a request next. Stay on the loader meanwhile:
        // signing out drops the cached account, which on its own would end the loading state a moment before
        // the navigation and show that page anyway.
        if (isSignInRequiredError(error) && endSignInThatExpired()) {
          setEndingExpiredSignIn(true);
          return;
        }
        setAuthBootstrapFailed(true);
      });
    }
  }, [hasMsalAccount, msalAccount, subId, instance]);

  useEffect(() => {
    if (profileLoaded && profileData?.data) {
      const { data } = profileData;
      const role = data.role as EnumUserRole;
      const mappedUser = {
        id: data.id,
        email: data.email,
        first_name: data.firstName,
        last_name: data.lastName,
        full_name: stringFromArray([data.firstName, data.lastName]),
        user_type: roleToAreaMap(role),
        user_role: role,
        status: (data.status as 'active' | 'deactivated') ?? 'active',
        updatedAt: data.updatedAt,
        organisation_id: data.organisationId,
        location_id: data.locationId,
      } as User;

      Sentry.setUser({
        id: data.id,
        email: data.email,
      });
      setAuthenticatedUserContext(data.id);
      setUserRole(role);
      setOrganisationName(data.organisationName);
      setUser(mappedUser);
    }
  }, [profileData, profileLoaded]);

  // The profile fetch failed — almost always a 401 (an expired/invalid token acquireTokenSilent could not
  // refresh, or the backend rejecting it outright) that the shared axios interceptor does not already turn
  // into a redirect, because it only recognises the specific SESSION_* envelopes. Left unhandled, nothing
  // ever gets this tab off whatever protected route it was on — isAuthLoading deliberately does not resolve
  // for this case (see its own comment below), because rendering ProtectedRoleRoute's fallback,
  // NotAuthorised, would tell someone who simply needs to sign in again that they lack permission instead.
  // Navigating away is the only way out, and /sign-in is where it leads: that page's own profileErrored
  // banner ("We cannot sign you in… sign in again") already exists for this exact case, so this effect only
  // needs to get the user there, not build a second one.
  //
  // Guarded on the current path rather than firing once: the profile query is enabled again as soon as a
  // fresh subId lands (a new sign-in), so a second failure must be able to send the user back here too.
  useEffect(() => {
    // Public policy pages are exempt for the same reason they skip the fetch: react-query keeps the last
    // error after the query is disabled, so a failure carried over from the previous route would otherwise
    // eject a reader from a document they are entitled to read signed out. The redirect still fires the
    // moment they navigate anywhere else.
    if (!profileErrored || location.pathname === ROUTES.public.signIn || isOnPublicPolicyPage) {
      return;
    }

    // Stale markers from the session that just failed must not survive the trip back to sign-in: a leftover
    // mfa_verified_at would wrongly suppress a fresh MFA prompt on the next sign-in, and a leftover
    // mfa_redirect_path would send a successful re-verification to a URL from the session that just broke.
    sessionStorage.removeItem('mfa_verified_at');
    sessionStorage.removeItem('mfa_redirect_path');
    setUser(null);
    setUserRole(null);
    setOrganisationName(null);
    // Disables the profile query (enabled: !!subId) so the stale error cannot keep firing, and — just as
    // important — lets the bootstrap effect above notice a *different* account once the user signs in again;
    // with subId left set to the old account's id, "hasMsalAccount && !subId" would never re-fire for it.
    setSubId(null);

    // Deliberately not a full MSAL sign-out (contrast signOut() below, which does call logoutRedirect): this
    // path is reached with a token acquireTokenSilent successfully refreshed, so CIAM's SSO session is fine —
    // it's our backend that rejected the profile fetch. If a future cause of this path turns out to be "CIAM
    // still has a live session for an account the backend will never accept," clicking "Sign in" here would
    // silently re-mint that same account with no credential prompt; today's known causes don't need one.
    navigate(ROUTES.public.signIn, { replace: true });
  }, [profileErrored, location.pathname, isOnPublicPolicyPage, navigate]);

  // Auth is "loading" in two windows:
  //   1. MSAL is still initializing — useIsAuthenticated() is false until
  //      initialize()/handleRedirectPromise() resolve, so we can't yet tell
  //      a signed-in user from a genuinely unauthorised one.
  //   2. MSAL has a signed-in account but our own profile/role hasn't landed.
  //      useIsAuthenticated() can briefly lag behind the initialized account
  //      cache, so use the account itself as the source of truth for this gate.
  // MFA pages and the public policy pages skip the profile fetch, so they're excluded from window 2 — leaving
  // them in would hang them on the spinner for ever, since nothing is coming to set userRole.
  //
  // Deliberately NOT resolved by profileErrored: the effect above is what gets a failed profile fetch off this
  // gate, by navigating to /sign-in — a route ProtectedRoleRoute never guards. Flipping isAuthLoading to false
  // here instead would let ProtectedRoleRoute render its *fallback* (NotAuthorised — "no role" is
  // indistinguishable from "role not loaded yet") for the one render before that effect's navigate takes
  // effect, flashing the wrong message. Leaving the loading state up for that one tick means the spinner
  // simply continues into the redirect instead.
  const isAuthLoading = !msalReady || endingExpiredSignIn || (hasMsalAccount && userRole === null && !isOnMfaPage && !isOnPublicPolicyPage && !authBootstrapFailed);

  const authContext: AuthContextItems = {
    user,
    userId: subId,
    userRole,
    organisationId: user?.organisation_id ?? null,
    organisationName,
    locationId: user?.location_id ?? null,
    isAuthLoading,
    profileErrored,
    features: profileData?.data?.features ?? NO_FEATURES,

    signIn: () => {
      // Clicking this button is the app's one reliable "the user is deliberately re-authenticating" signal, so
      // it is where the signed-out-account marker stands down (BLUM-7241). Deliberately not hung on
      // handleRedirectPromise() resolving non-null: that returns null whenever msal.interaction.status is
      // missing from this tab's sessionStorage, which would leave the marker set for good and lock the user out
      // of /mfa/verify entirely — a worse failure than the one the marker exists to prevent.
      //
      // A stand-down rather than a clear, because the click says the user *intends* to re-authenticate, not
      // that they did: an abandoned redirect — Back at the CIAM screen, a closed account picker, a bounce from
      // the IdP — is not reliably reported (MSAL rejects only when Back restores this page from the bfcache),
      // so a clear here would be permanent and this account's guard would be off from then on. It also has to
      // work when no account is cached, which is the normal state on this page:
      // the sign-out that set the marker ran logoutRedirect, which wiped MSAL's cache. That rules out scoping
      // the stand-down to an account id — there is often no id to scope it to, and the user who cannot stand
      // their own marker down cannot get back in. releaseWindowMs bounds what the unscoped release costs.
      const forcedSignOut = readSignedOutAccount();
      releaseSignedOutAccount();

      const redirectUri = import.meta.env.BASE_URL || '/';

      return instance
        .loginRedirect({
          ...loginRequest,
          redirectUri,
          // MsalProvider calls handleRedirectPromise() too, with no options, and MSAL keeps whichever call ran
          // first. Naming the redirect URI as the start page makes the two agree: the response is processed
          // where it lands whether or not msalInitPromise's navigateToLoginRequestUrl: false won that race.
          redirectStartPage: redirectUri,
          // Releasing the marker on a click is only safe if the click cannot be answered by the IdP silently.
          // When a forced sign-out is outstanding the CIAM session is very likely still live — that is the
          // failure that left the marker behind — so SSO would hand back the same account with no credential
          // prompt at all, and the user would reach /mfa/verify having re-authenticated with nothing.
          ...(forcedSignOut ? { prompt: PromptValue.LOGIN } : {}),
        })
        .catch((error) => {
          // The redirect never left the page, or Back brought this page straight back from the bfcache, so nobody
          // re-authenticated and the sign-out is still outstanding. Re-assert now rather than letting a window
          // we already know released nobody run its course.
          if (forcedSignOut) {
            reassertSignedOutAccount();
          }
          console.log(error);
          // Passed on so the page can give its button back; nothing else is waiting on this.
          throw error;
        });
    },
    signOut: async () => {
      try {
        // Destroys the server-side mfa_sessions row, not just the cookie (BLUM-7242).
        await mfaLogout();
      } catch (error) {
        // The server-side session may have survived, so don't fail silently — but still complete the
        // IdP sign-out rather than stranding the user signed in.
        Sentry.captureException(error);
      }

      // None of these keys are cleared anywhere else; a stale mfa_redirect_path would otherwise land the
      // next user in this tab on the previous user's last URL, and a stale session_ended_reason would tell
      // them they had been timed out when they had simply signed out (BLUM-7238).
      sessionStorage.removeItem('mfa_verified_at');
      sessionStorage.removeItem('mfa_redirect_path');
      clearSessionEndedReason();
      // Signing out on purpose supersedes having been signed out; leaving the marker would suppress the MFA
      // prompt for the next person who signs in on this browser as the same account.
      clearSignedOutAccount();
      setSubId(null);
      setUser(null);
      setOrganisationName(null);

      // Passing the account sends logout_hint, so CIAM ends this account's SSO session directly instead
      // of prompting with an account picker. Reuse the account the provider authenticates with rather
      // than re-deriving one, so we can't sign out of a different cached account than the app was using.
      await instance.logoutRedirect({
        account: msalAccount ?? null,
        postLogoutRedirectUri: import.meta.env.BASE_URL || '/',
      });
    },
  };

  return <AuthContext.Provider value={authContext}>{children}</AuthContext.Provider>;
};

export default AuthProvider;

export const useAuthProvider = () => {
  const authContext = useContext(AuthContext);
  if (authContext === null) {
    throw new Error('No AuthContext');
  }

  return authContext;
};
