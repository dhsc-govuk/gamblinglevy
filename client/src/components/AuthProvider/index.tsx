import React, { useContext, useEffect, useState } from 'react';
import { useMsal } from '@azure/msal-react';
import { LogLevel, PromptValue, PublicClientApplication } from '@azure/msal-browser';
import { useLocation } from 'react-router-dom';
import { useQuery } from 'react-query';
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

export const msalConfig = {
  auth: {
    clientId: config.clientId,
    authority: config.authority,
    knownAuthorities: [new URL(config.authority).hostname],
    redirectUri: config.redirectUri,
    postLogoutRedirectUri: import.meta.env.BASE_URL || '/',
    navigateToLoginRequestUrl: false,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      loggerCallback: (level: any, message: any, containsPii: any) => {
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
};

export const loginRequest = {
  scopes: config.scopes,
};

// MSAL v3 requires explicit initialize() + handleRedirectPromise() before any
// token acquisition. Centralising both here lets AuthProvider gate its loading
// signal on init completion (otherwise the auth guard renders 403 during the
// brief window before MSAL has parsed sessionStorage / the redirect hash).
export const msalInstance = new PublicClientApplication(msalConfig);

let msalReadyResolved = false;
export const msalInitPromise = msalInstance
  .initialize()
  .then(() => msalInstance.handleRedirectPromise())
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
  // Used for grouped tabs like "Management" that span several sub-routes (users, forms, etc.).
  activePrefixes?: string[];
}

// Header service-navigation items per role. Links must resolve to routes the role can actually
// reach (see Routes.tsx) — otherwise non-admin users hit the 404 page.
export const navigationForRole = (role: EnumUserRole | null): HeaderNavItem[] => {
  switch (role) {
    case EnumUserRole.ADMIN:
      return [
        { label: 'Reports', href: ROUTES.admin.reports.list },
        { label: 'Trends', href: ROUTES.admin.trends },
        {
          label: 'Management',
          href: ROUTES.admin.management,
          activePrefixes: [ROUTES.admin.collections.list, ROUTES.admin.users.list, ROUTES.admin.forms.list, ROUTES.admin.organisations.list, ROUTES.admin.guidance],
        },
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
  locationId: string | null;
  // True while MSAL is resolving its session OR the profile query is in flight.
  // Lets gating components (e.g. ProtectedRoleRoute) show a loading screen instead
  // of mis-classifying a not-yet-loaded user as unauthorised.
  isAuthLoading: boolean;
  // The profile fetch came back and failed. Distinguishes "we do not have a role yet" from "we are never
  // going to get one", which the absence of userRole on its own cannot: the query is retry: false, so a
  // screen that waits for a role it will never receive waits forever.
  profileErrored: boolean;
  signIn: () => void;
  signOut: () => Promise<void>;
}

export const AuthContext = React.createContext<AuthContextItems | null>(null);

type Props = { children: React.ReactNode };

const AuthProvider = ({ children }: Props): React.ReactElement => {
  const { instance, accounts } = useMsal();
  const [subId, setSubId] = useState<string | null>(null);
  const [user, setUser] = useState<User | null>(null);
  const [userRole, setUserRole] = useState<EnumUserRole | null>(null);
  const [authBootstrapFailed, setAuthBootstrapFailed] = useState(false);
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
  const isOnMfaPage = location.pathname.startsWith('/mfa/');
  const msalAccount = accounts[0] ?? (msalReady ? instance.getAllAccounts()[0] : undefined);
  const hasMsalAccount = Boolean(msalAccount);
  const {
    data: profileData,
    isSuccess: profileLoaded,
    isError: profileErrored,
  } = useQuery({
    queryKey: profileDateCacheKey,
    queryFn: getProfile,
    enabled: !!subId && !isOnMfaPage,
    staleTime: defaultStaleTime,
    retry: false,
  });

  useEffect(() => {
    const signIn = async () => {
      if (msalAccount) {
        const tokenRequest = { ...loginRequest, account: msalAccount };
        const response = await instance.acquireTokenSilent(tokenRequest);
        setAuthBootstrapFailed(false);
        setSubId(response.account?.localAccountId ?? '');
      }
    };
    if (hasMsalAccount && !subId) {
      signIn().catch((error) => {
        console.error(error);
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
      setUser(mappedUser);
    }
  }, [profileData, profileLoaded]);

  // Auth is "loading" in two windows:
  //   1. MSAL is still initializing — useIsAuthenticated() is false until
  //      initialize()/handleRedirectPromise() resolve, so we can't yet tell
  //      a signed-in user from a genuinely unauthorised one.
  //   2. MSAL has a signed-in account but our own profile/role hasn't landed.
  //      useIsAuthenticated() can briefly lag behind the initialized account
  //      cache, so use the account itself as the source of truth for this gate.
  // MFA pages skip the profile fetch, so they're excluded from window 2.
  const isAuthLoading = !msalReady || (hasMsalAccount && userRole === null && !isOnMfaPage && !authBootstrapFailed);

  const authContext: AuthContextItems = {
    user,
    userId: subId,
    userRole,
    organisationId: user?.organisation_id ?? null,
    locationId: user?.location_id ?? null,
    isAuthLoading,
    profileErrored,

    signIn: () => {
      // Clicking this button is the app's one reliable "the user is deliberately re-authenticating" signal, so
      // it is where the signed-out-account marker stands down (BLUM-7241). Deliberately not hung on
      // handleRedirectPromise() resolving non-null: that returns null whenever msal.interaction.status is
      // missing from this tab's sessionStorage, which would leave the marker set for good and lock the user out
      // of /mfa/verify entirely — a worse failure than the one the marker exists to prevent.
      //
      // A stand-down rather than a clear, because the click says the user *intends* to re-authenticate, not
      // that they did: an abandoned redirect — Back at the CIAM screen, a closed account picker, a bounce from
      // the IdP — does not reject, so a clear here would be permanent and this account's guard would be off
      // from then on. It also has to work when no account is cached, which is the normal state on this page:
      // the sign-out that set the marker ran logoutRedirect, which wiped MSAL's cache. That rules out scoping
      // the stand-down to an account id — there is often no id to scope it to, and the user who cannot stand
      // their own marker down cannot get back in. releaseWindowMs bounds what the unscoped release costs.
      const forcedSignOut = readSignedOutAccount();
      releaseSignedOutAccount();

      instance
        .loginRedirect({
          ...loginRequest,
          redirectUri: import.meta.env.BASE_URL || '/',
          // Releasing the marker on a click is only safe if the click cannot be answered by the IdP silently.
          // When a forced sign-out is outstanding the CIAM session is very likely still live — that is the
          // failure that left the marker behind — so SSO would hand back the same account with no credential
          // prompt at all, and the user would reach /mfa/verify having re-authenticated with nothing.
          ...(forcedSignOut ? { prompt: PromptValue.LOGIN } : {}),
        })
        .catch((error) => {
          // The redirect never left the page, so nobody re-authenticated and the sign-out is still outstanding.
          // Re-assert now rather than letting a window we already know released nobody run its course.
          if (forcedSignOut) {
            reassertSignedOutAccount();
          }
          console.log(error);
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
