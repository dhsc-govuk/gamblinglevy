import { useState, useEffect, useLayoutEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { useIsAuthenticated } from '@azure/msal-react';
import usePageTitle from '../../../hooks/usePageTitle';
import { roleToAreaMap, useAuthProvider } from '../../../components/AuthProvider';
import { GovUKButton, GovUKDetails, GovUKNotificationBanner, LoadingBox, OrderedList, ListItem, Paragraph } from '../../../components/GovUKComponents';
import SignInJourneyLayout from '../../../layouts/SignInJourney';
import { clearSessionEndedReason, readSessionEndedReason, type SessionEndedReason } from '../../../helpers/sessionEnd';
import { composeTestId } from '../../../helpers/testIds';
import './styles.css';

// Keyed rather than a chain of ternaries, so adding a reason to SessionEndedReason is a type error here
// instead of silently falling through to whichever message happens to be last — which is what the old `else`
// branch did, quietly describing every new reason as the 8-hour cap.
//
// Three of these numbers are published copy that has to agree with ApplicationSettings.MfaSettings: the
// 30-minute idle window (IdleTimeoutMinutes), the 8-hour cap (SessionExpiryHours) and now the 30-minute
// lockout (LockoutMinutes). The class comment on MfaSettings names this file for that reason — the client
// has no unauthenticated endpoint to read them from, so they are stated in both places or they drift.
const SESSION_ENDED_MESSAGES: Record<SessionEndedReason, string> = {
  idle: 'For your security we signed you out because you were inactive for 30 minutes. Sign in again to continue.',
  revoked:
    'This session was ended \u2014 either by you from another device, by an administrator, or because you were signed in on more devices than we allow. Sign in again to continue. If you do not recognise a device on your account, tell your administrator.',
  expired: 'For your security we sign you out 8 hours after you sign in. Sign in again to continue.',
  // Deliberately not the 'revoked' wording: that copy offers three explanations, none of which is what
  // happened, and it would leave someone who is locked out waiting for a sign-in that cannot succeed yet.
  // "try again" rather than "sign in again" because CIAM sign-in is not what is blocked \u2014 entering the code is.
  locked: 'For your security we signed you out after too many incorrect security codes. You can try again in 30 minutes. If you did not try to sign in, tell your administrator.',
};

const SignIn = () => {
  usePageTitle('Sign in');
  const navigate = useNavigate();
  const isAuthenticated = useIsAuthenticated();
  const { signIn, userRole, profileErrored } = useAuthProvider();
  const [signingIn, setSigningIn] = useState<boolean>(false);

  // Written by the axios interceptor just before it ends the CIAM sign-in (BLUM-7238). Read in the
  // initialiser so the banner is there on first paint, but cleared in the effect below rather than during
  // render — a discarded StrictMode render would otherwise consume the key and lose the message for good.
  const [sessionEndedReason, showSessionEndedReason] = useState(() => readSessionEndedReason());
  const sessionEndedBannerRef = useRef<HTMLDivElement>(null);
  const profileErrorBannerRef = useRef<HTMLDivElement>(null);

  // A stored reason means the server already told us this session is over, so we are emphatically not
  // mid-sign-in — even though MSAL may still hold an account because logoutRedirect failed. Without this the
  // page renders the "Signing you in…" box forever: the profile query 401s and never sets a role, so
  // isAuthenticated && !userRole stays true with no way out, and the banner explaining why is never shown.
  //
  // profileErrored closes the same trap on a reload, where the reason has already been consumed by the effect
  // below and there is nothing left to read (BLUM-7241). The query is retry: false, so a failed profile fetch
  // — a 403 whose redirect to /mfa/verify is now suppressed, or any 500 — means no role is ever coming, and
  // waiting for one is waiting forever.
  const isCompletingSignIn = isAuthenticated && !userRole && !sessionEndedReason && !profileErrored;

  useEffect(() => {
    if (!sessionEndedReason) {
      return;
    }

    clearSessionEndedReason();

    // We arrive here through a client-side route change (Splash redirects on mount), and an "important"
    // banner is a region rather than a live region, so nothing announces it. Moving real focus is what
    // tells a screen-reader or keyboard user why they were ejected mid-task.
    sessionEndedBannerRef.current?.focus();
  }, [sessionEndedReason]);

  // Same reasoning, for the other way this page can be reached with nothing to sign in to: the profile
  // fetch failed and no reason was stored — a service outage, or a reload after the stored reason was
  // consumed above. Without this the page is an ordinary sign-in screen that quietly loops.
  useLayoutEffect(() => {
    if (!profileErrored || sessionEndedReason) {
      return;
    }

    // Re-read before falling back to the generic banner. The 403 that ends this page's sign-in attempt lands
    // *after* mount — the profile query only enables once acquireTokenSilent has resolved a subId — so on a
    // reload the interceptor writes the reason a beat too late for the initialiser above to have seen it.
    // Without this the user is told "we cannot sign you in … tell your administrator": a service-fault message
    // naming the wrong culprit, when nothing is broken and the correct action was simply to sign in again.
    // Promoting it into state re-runs the effect above, which clears the key and moves focus to that banner.
    //
    // Layout rather than passive: profileErrored is evaluated during render, so a passive effect would let the
    // wrong banner paint for a frame before swapping it — a visible flash of the exact message this exists to
    // suppress. This effect only reads; clearSessionEndedReason stays in the passive effect above, so a
    // discarded StrictMode render still cannot consume the key.
    const announced = readSessionEndedReason();

    if (announced) {
      showSessionEndedReason(announced);
      return;
    }

    profileErrorBannerRef.current?.focus();
  }, [profileErrored, sessionEndedReason]);

  useEffect(() => {
    if (userRole) {
      const area = roleToAreaMap(userRole);
      navigate(`/${area}/home`);
    }
  }, [userRole, navigate]);

  const handleSignIn = () => {
    setSigningIn(true);
    // signIn settles only when the redirect did not happen: MSAL rejects when it cannot start one, when Back
    // at the Microsoft screen restores this page from the bfcache, and after 30 seconds still on the page.
    // Without this the page's only button stays in its loading state until a reload.
    signIn().catch(() => setSigningIn(false));
  };

  return (
    <SignInJourneyLayout testId="public-signin">
      {isCompletingSignIn ? (
        <div className="govuk-main-wrapper">
          <LoadingBox loading={true}>
            <div className="signin-page__loading" data-testid="public-signin-loading">
              <p className="govuk-body">Signing you in. This may take a moment if the service is starting up.</p>
            </div>
          </LoadingBox>
        </div>
      ) : (
        <>
          <a href="https://www.gov.uk" className="govuk-back-link" data-testid="public-signin-back">
            Back
          </a>
          <div className="govuk-main-wrapper" style={{ paddingTop: '10px' }}>
            <div className="signin-page__content">
              {sessionEndedReason && (
                <div ref={sessionEndedBannerRef} tabIndex={-1} className="signin-page__banner-focus-target" data-testid="public-signin-session-ended-banner-focus-target">
                  <GovUKNotificationBanner type="important" title="You have been signed out" testId="public-signin-session-ended-banner">
                    <Paragraph data-testid={composeTestId('public-signin-session-ended-banner', sessionEndedReason, 'message')}>
                      {SESSION_ENDED_MESSAGES[sessionEndedReason]}
                    </Paragraph>
                  </GovUKNotificationBanner>
                </div>
              )}

              {profileErrored && !sessionEndedReason && (
                <div ref={profileErrorBannerRef} tabIndex={-1} className="signin-page__banner-focus-target" data-testid="public-signin-profile-error-banner-focus-target">
                  <GovUKNotificationBanner type="important" title="We cannot sign you in" testId="public-signin-profile-error-banner">
                    <Paragraph data-testid="public-signin-profile-error-banner-message">
                      We could not load your account details. Sign in again to continue. If this keeps happening, tell your administrator.
                    </Paragraph>
                  </GovUKNotificationBanner>
                </div>
              )}

              <h1 className="govuk-heading-xl">Sign in to Share progress preventing gambling harms</h1>

              <p className="govuk-body">To keep your information secure, you&rsquo;ll need to complete 2 steps to sign in.</p>

              <OrderedList>
                <ListItem>Verify your email address with Microsoft.</ListItem>
                <ListItem>Enter a security code from your authenticator app.</ListItem>
              </OrderedList>

              <GovUKButton isLoading={signingIn} onClick={handleSignIn} data-testid="public-signin-continue">
                Continue to Microsoft sign in
              </GovUKButton>

              <h2 className="govuk-heading-m">If this is your first time signing in</h2>
              <p className="govuk-body">You&rsquo;ll need to set up your account security before you can use the service.</p>

              <GovUKDetails summary="Help with setting up your account" data-testid="public-signin-help-details">
                <h3 className="govuk-heading-s">Verify your email address with Microsoft</h3>
                <p className="govuk-body">
                  You&rsquo;ll be taken to a Microsoft screen to enter your email. Microsoft will send a security code to your inbox for you to enter on the next screen. This will
                  verify your email address.
                </p>
                <h3 className="govuk-heading-s">Set up an authenticator app</h3>
                <p className="govuk-body">
                  You&rsquo;ll return to this service to set up an authenticator app on your smartphone. This is to help confirm it&rsquo;s you when you sign in.
                </p>
              </GovUKDetails>
            </div>
          </div>
        </>
      )}
    </SignInJourneyLayout>
  );
};

export default SignIn;
