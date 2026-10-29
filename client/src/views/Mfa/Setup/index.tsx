import React, { useState, useEffect, useLayoutEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { QRCodeSVG } from 'qrcode.react';
import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { httpMessageOf, httpStatusOf } from '../../../helpers/httpErrors';
import {
  ErrorSummary,
  GovUKButton,
  GovUKDetails,
  GovUKBackLink,
  GovUKFieldset,
  GovUKWarningText,
  OrderedList,
  PageLoading,
  UnorderedList,
  ListItem,
} from '../../../components/GovUKComponents';
import usePageTitle, { SERVICE_NAME } from '../../../hooks/usePageTitle';
import scrollToTop from '../../../helpers/scrollToTop';
import { useMfa } from '../../../components/MfaProvider';
import { useAuthProvider, roleToAreaMap } from '../../../components/AuthProvider';
import SignInJourneyLayout from '../../../layouts/SignInJourney';
import { mfaCodeError, mfaCodeRefusalMessage, sanitiseMfaCode } from '../messages';
import RecoveryCodesList from '../../../components/Global/Mfa/RecoveryCodesList';
import SigningInHelp from '../../../components/Global/Mfa/SigningInHelp';
import '../styles.css';

const STEP_TITLES = ['Set up an authenticator app', 'Check your authenticator app for a security code', 'Save your recovery codes'];

const MfaSetup = (): React.ReactElement => {
  const navigate = useNavigate();
  const { inProgress, accounts } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const { userRole, signOut } = useAuthProvider();
  const { setupData, recoveryCodes, initiateSetup, completeSetup, clearRecoveryCodes, isEnabled } = useMfa();

  const isMsalLoading = inProgress !== InteractionStatus.None || (isAuthenticated && accounts.length === 0);

  const [activeStep, setActiveStep] = useState(0);
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [confirmSaved, setConfirmSaved] = useState(false);
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const [submitAttempts, setSubmitAttempts] = useState(0);
  const [isVerifying, setIsVerifying] = useState(false);
  const [isInitializing, setIsInitializing] = useState(true);
  const userRoleRef = useRef(userRole);

  // Setup could not be started: the only error there can be before there is a QR code to show. A code refusal
  // that arrives after the user has gone Back to step 0 is not this — by then setupData exists — and is not shown
  // on this step; Continue clears it.
  const initFailed = !isInitializing && activeStep === 0 && !setupData && !!error;

  // Which error the one page-level summary shows. On step 0 that can only be a failure to start setup, which has
  // no field to correct, so its link goes to the step's heading.
  const stepErrors =
    initFailed && error
      ? [{ targetName: 'mfa-setup-heading', text: error }]
      : activeStep === 1 && error
      ? [{ targetName: 'code-input', text: error }]
      : activeStep === 2 && confirmError
      ? [{ targetName: 'confirm-saved', text: confirmError }]
      : [];

  usePageTitle(stepErrors.length > 0 ? `Error: ${STEP_TITLES[activeStep]}` : STEP_TITLES[activeStep]);

  // Setup steps swap in place (no route change) — reset the visual position and move focus to the new
  // step's heading so keyboard/screen-reader users start from the top of the replacement content rather
  // than wherever focus was left on the previous step.
  useLayoutEffect(() => {
    scrollToTop();
    document.getElementById('mfa-setup-heading')?.focus({ preventScroll: true });
  }, [activeStep]);

  useEffect(() => {
    if (userRole) {
      userRoleRef.current = userRole;
    }
  }, [userRole]);

  // The setup effect below calls initiateSetup through a ref, never as a dependency. MfaProvider rebuilds that
  // function on every render (it closes over the mutation object), and starting setup is itself what re-renders
  // the provider — so as a dependency it would re-run the effect and send POST mfa/setup again on every pass.
  // Declared above that effect so the ref is current before it runs.
  const initiateSetupRef = useRef(initiateSetup);
  useEffect(() => {
    initiateSetupRef.current = initiateSetup;
  });

  useEffect(() => {
    if (activeStep === 2 || recoveryCodes.length > 0) {
      return;
    }

    if (isMsalLoading) {
      return;
    }

    if (!isAuthenticated) {
      navigate('/sign-in');
      return;
    }

    if (isEnabled) {
      const homeRoute = userRole ? `/${roleToAreaMap(userRole)}/home` : '/';
      navigate(homeRoute);
      return;
    }

    if (setupData) {
      setIsInitializing(false);
      return;
    }

    let isMounted = true;

    const initSetup = async (retryCount = 0): Promise<void> => {
      try {
        await initiateSetupRef.current();
      } catch (err) {
        if (!isMounted) return;

        const status = httpStatusOf(err);
        if (status === 401 && retryCount < 3) {
          setTimeout(() => initSetup(retryCount + 1), 1000);
          return;
        }

        if (status === 409) {
          const homeRoute = userRole ? `/${roleToAreaMap(userRole)}/home` : '/';
          navigate(homeRoute);
        } else {
          setError(httpMessageOf(err) ?? 'There was a problem setting up the authenticator app. Try again.');
        }
      } finally {
        if (isMounted) {
          setIsInitializing(false);
        }
      }
    };

    initSetup();

    return () => {
      isMounted = false;
    };
  }, [isMsalLoading, isAuthenticated, isEnabled, navigate, userRole, setupData, accounts.length, activeStep, recoveryCodes.length]);

  const handleCodeChange = useCallback((e: React.ChangeEvent<HTMLInputElement>): void => {
    setCode(sanitiseMfaCode(e.target.value));
    setError(null);
  }, []);

  const handleVerify = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault();

    setSubmitAttempts((n) => n + 1);

    const codeError = mfaCodeError(code);
    if (codeError) {
      setError(codeError);
      return;
    }

    setIsVerifying(true);
    setError(null);

    try {
      await completeSetup(code);
      setActiveStep(2);
    } catch (err) {
      setError(mfaCodeRefusalMessage(err, 'Verification failed. Try again.', 'Your setup session has expired. Refresh the page to start again.'));
    } finally {
      setIsVerifying(false);
    }
  };

  const handleFinish = (): void => {
    setSubmitAttempts((n) => n + 1);

    if (!confirmSaved) {
      setConfirmError('Select the box to confirm you have saved your codes');
      return;
    }

    clearRecoveryCodes();
    const role = userRoleRef.current || userRole;
    const homeRoute = role ? `/${roleToAreaMap(role)}/home` : '/sign-in';
    navigate(homeRoute);
  };

  // The summary takes focus when its trigger changes after it has mounted, and it mounts in the very update that
  // reports this failure (the loading screen above it gives way at the same moment). So the bump for a failed
  // start is made here, one render later, where the summary can see it.
  useEffect(() => {
    if (initFailed) {
      setSubmitAttempts((n) => n + 1);
    }
  }, [initFailed]);

  // One layout for both states, so the header and footer stay mounted when loading finishes. The header keeps one
  // testId across them — "the crown link is there whatever the init state" is what a test wants — while the root's
  // says which state the page is in.
  return (
    <SignInJourneyLayout testId={isInitializing ? 'mfa-setup-loading' : 'mfa-setup'} headerTestId="mfa-setup-header">
      {isInitializing ? (
        <PageLoading label="Setting up your authenticator app" testId="mfa-setup-loading-status" />
      ) : (
        <div className="govuk-main-wrapper">
          {activeStep === 0 && (
            <GovUKBackLink onClick={signOut} data-testid="mfa-setup-step-0-back">
              Back
            </GovUKBackLink>
          )}
          {activeStep === 1 && (
            <GovUKBackLink
              onClick={() => {
                setCode('');
                setError(null);
                setActiveStep(0);
              }}
              data-testid="mfa-setup-verify-back">
              Back
            </GovUKBackLink>
          )}

          {/* One summary for all three steps. It is always mounted because it has to exist before an error
              arrives to be focused for it (see GovUKErrorSummary). */}
          <ErrorSummary testId="mfa-setup-error-summary" heading="There is a problem" errors={stepErrors} focusTrigger={submitAttempts} />

          {/* Step 1: Set up an authenticator app */}
          {activeStep === 0 && (
            <div className="mfa-page__content-wrapper">
              <h1 className="govuk-heading-xl mfa-page__focus-target" id="mfa-setup-heading" tabIndex={-1}>
                Set up an authenticator app
              </h1>

              <p className="govuk-body">
                To keep your account secure, you need to use an authenticator app on your smartphone. You&rsquo;ll be asked to enter a 6-digit code from your authenticator app each
                time you sign in.
              </p>

              <p className="govuk-body">Download the Microsoft Authenticator or Google Authenticator app on your phone.</p>

              <p className="govuk-body">Open the app and scan the QR code.</p>

              {setupData && (
                <>
                  <div className="mfa-page__qr-wrapper">
                    <QRCodeSVG value={setupData.qrCodeUri} size={200} level="M" role="img" aria-label="QR code for setting up your authenticator app" />
                  </div>

                  <GovUKDetails summary="I cannot scan the QR code" data-testid="mfa-setup-manual-key-details">
                    <p className="govuk-body">You can enter a code or &lsquo;setup key&rsquo; into your authenticator app instead.</p>
                    <OrderedList>
                      <ListItem>Open your authenticator app and select the option to add a new account.</ListItem>
                      <ListItem>Choose to enter a code or setup key, instead of scanning a QR code.</ListItem>
                      <ListItem>
                        Enter these details.
                        <UnorderedList>
                          <ListItem>Account name: {SERVICE_NAME}</ListItem>
                          <ListItem>
                            Code:
                            <div className="mfa-page__manual-key">{setupData.manualEntryKey}</div>
                          </ListItem>
                        </UnorderedList>
                      </ListItem>
                    </OrderedList>
                  </GovUKDetails>

                  <GovUKButton
                    onClick={() => {
                      // A code refusal can land here after Back was pressed with the request still in flight. It
                      // belongs to a code that has been abandoned, so it is dropped rather than carried into step 1.
                      setError(null);
                      setActiveStep(1);
                    }}
                    data-testid="mfa-setup-step-0-continue">
                    Continue
                  </GovUKButton>
                </>
              )}

              {/* Between a 401 retry and its next attempt: initialising is over but there is no QR code yet. */}
              {!setupData && !error && <PageLoading label="Setting up your authenticator app" testId="mfa-setup-step-0-loading" />}
            </div>
          )}

          {/* Step 2: Check your authenticator app for a security code */}
          {activeStep === 1 && (
            <div className="mfa-page__content-wrapper">
              <h1 className="govuk-heading-xl mfa-page__focus-target" id="mfa-setup-heading" tabIndex={-1}>
                Check your authenticator app for a security code
              </h1>

              {/* A form, so Enter in the code field submits it like the button does. */}
              <form noValidate onSubmit={handleVerify}>
                <GovUKFieldset.Input
                  id="code-input"
                  name="code"
                  label="Security code"
                  hint="This is a 6-digit code, for example 654321"
                  value={code}
                  onChange={handleCodeChange}
                  error={error ?? undefined}
                  width="5"
                  disabled={isVerifying}
                  autoComplete="one-time-code"
                  inputMode="numeric"
                  spellCheck={false}
                  testId="mfa-setup-code-input"
                />

                <GovUKButton type="submit" isLoading={isVerifying} data-testid="mfa-setup-verify-continue">
                  Continue
                </GovUKButton>
              </form>

              <SigningInHelp testId="mfa-setup-help" />
            </div>
          )}

          {/* Step 3: Save your recovery codes */}
          {activeStep === 2 && recoveryCodes.length > 0 && (
            <div className="mfa-page__content-wrapper">
              <h1 className="govuk-heading-xl mfa-page__focus-target" id="mfa-setup-heading" tabIndex={-1}>
                Save your recovery codes
              </h1>

              <p className="govuk-body">If you lose your smartphone or cannot use your authenticator app, you&rsquo;ll need a recovery code to sign in to your account.</p>

              <p className="govuk-body">Each code can only be used once. You should keep these codes safe.</p>

              <GovUKWarningText data-testid="mfa-setup-recovery-warning">You will not be able to see these codes again once you leave this screen.</GovUKWarningText>

              <h2 className="govuk-heading-l">Your recovery codes</h2>

              <RecoveryCodesList codes={recoveryCodes} testId="mfa-setup-recovery" />

              <GovUKFieldset.Checkbox
                id="confirm-saved"
                name="confirm-saved"
                label="I have saved my recovery codes in a secure place."
                size="small"
                checked={confirmSaved}
                error={confirmError ?? undefined}
                onChange={(ev) => {
                  setConfirmSaved(ev.target.checked);
                  if (ev.target.checked) setConfirmError(null);
                }}
                testId="mfa-setup-confirm-saved"
              />

              <GovUKButton onClick={handleFinish} data-testid="mfa-setup-finish">
                Continue
              </GovUKButton>
            </div>
          )}
        </div>
      )}
    </SignInJourneyLayout>
  );
};

export default MfaSetup;
