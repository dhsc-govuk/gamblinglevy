import React, { useState, useEffect, useLayoutEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { CircularProgress } from '@mui/material';
import { QRCodeSVG } from 'qrcode.react';
import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { Button, InputField, WarningText, LoadingBox } from 'govuk-react';
import { ErrorSummary, GovUKButton, GovUKSkipLink, GovUKDetails, GovUKBackLink, OrderedList, UnorderedList, ListItem } from '../../../components/GovUKComponents';
import usePageTitle from '../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../hooks/useErrorSummaryFocus';
import scrollToTop from '../../../helpers/scrollToTop';
import { useMfa } from '../../../components/MfaProvider';
import { useAuthProvider, roleToAreaMap } from '../../../components/AuthProvider';
import { GovUKHeaderLogo } from '../../../components/Logos';
import LayoutFooter from '../../../layouts/General/LayoutFooter';
import '../styles.css';

const SERVICE_NAME = 'Share progress preventing gambling harms';

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

  usePageTitle(STEP_TITLES[activeStep]);
  useErrorSummaryFocus(submitAttempts, !!error || !!confirmError);

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
        await initiateSetup();
      } catch (err: any) {
        if (!isMounted) return;

        if (err?.response?.status === 401 && retryCount < 3) {
          setTimeout(() => initSetup(retryCount + 1), 1000);
          return;
        }

        if (err?.response?.status === 409) {
          const homeRoute = userRole ? `/${roleToAreaMap(userRole)}/home` : '/';
          navigate(homeRoute);
        } else {
          const errorMessage = err?.response?.data?.message || err?.message || 'There was a problem setting up the authenticator app. Try again.';
          setError(errorMessage);
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
    // initiateSetup intentionally omitted — its identity changes on every MfaProvider render
    // (depends on setupMutation), and including it would loop the effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isMsalLoading, isAuthenticated, isEnabled, navigate, userRole, setupData, accounts.length, activeStep, recoveryCodes.length]);

  const handleCodeChange = useCallback((e: React.ChangeEvent<HTMLInputElement>): void => {
    const value = e.target.value.replace(/\D/g, '').slice(0, 6);
    setCode(value);
    setError(null);
  }, []);

  const handleVerify = async (e?: React.FormEvent): Promise<void> => {
    if (e) e.preventDefault();

    setSubmitAttempts((n) => n + 1);

    if (code.length === 0) {
      setError('Enter the 6-digit code shown in your authenticator app');
      return;
    }

    if (code.length !== 6) {
      setError('The code must be 6 digits. Check the code and try again');
      return;
    }

    setIsVerifying(true);
    setError(null);

    try {
      await completeSetup(code);
      setActiveStep(2);
    } catch (err: any) {
      if (err?.response?.status === 401) {
        setError('That code no longer works. You need to enter the 6-digit code currently shown in your app');
      } else if (err?.response?.status === 400) {
        setError('Your setup session has expired. Refresh the page to start again.');
      } else {
        setError('Verification failed. Try again.');
      }
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

  const handleCopyRecoveryCodes = (): void => {
    navigator.clipboard.writeText(recoveryCodes.join('\n'));
  };

  const handleDownloadRecoveryCodes = (): void => {
    const blob = new Blob([recoveryCodes.join('\n')], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'recovery-codes.txt';
    a.click();
    URL.revokeObjectURL(url);
  };

  const handleKeyPress = (e: React.KeyboardEvent): void => {
    if (e.key === 'Enter') {
      e.preventDefault();
      handleVerify();
    }
  };

  if (isInitializing) {
    return (
      <div className="mfa-page">
        <GovUKSkipLink />
        <header className="govuk-header" data-module="govuk-header">
          <div className="govuk-header__container govuk-width-container">
            <div className="govuk-header__logo">
              <a href="https://www.gov.uk" className="govuk-header__link govuk-header__link--homepage">
                <GovUKHeaderLogo />
              </a>
            </div>
          </div>
        </header>
        <main className="mfa-page__main" id="main-content" role="main">
          <div className="govuk-width-container">
            <div className="mfa-page__loading">
              <LoadingBox loading={true}>
                <div style={{ minHeight: '200px' }} />
              </LoadingBox>
            </div>
          </div>
        </main>
        <LayoutFooter />
      </div>
    );
  }

  return (
    <div className="mfa-page" data-testid="mfa-setup">
      <GovUKSkipLink />
      <header className="govuk-header" data-module="govuk-header">
        <div className="govuk-header__container govuk-width-container">
          <div className="govuk-header__logo">
            <a href="https://www.gov.uk" className="govuk-header__link govuk-header__link--homepage">
              <GovUKHeaderLogo />
            </a>
          </div>
        </div>
      </header>

      <section className="mfa-page__service-banner" aria-label="Service information">
        <div className="govuk-width-container">
          <span className="mfa-page__service-name">{SERVICE_NAME}</span>
        </div>
      </section>

      <main className="mfa-page__main" id="main-content" role="main">
        <div className="govuk-width-container">
          <div className="govuk-main-wrapper">
            {activeStep === 0 && <GovUKBackLink onClick={signOut}>Back</GovUKBackLink>}
            {activeStep === 1 && (
              <GovUKBackLink
                onClick={() => {
                  setCode('');
                  setError(null);
                  setActiveStep(0);
                }}>
                Back
              </GovUKBackLink>
            )}

            {activeStep === 1 && error && <ErrorSummary heading="There is a problem" errors={[{ targetName: 'code-input', text: error }]} />}
            {activeStep === 2 && confirmError && <ErrorSummary heading="There is a problem" errors={[{ targetName: 'confirm-saved', text: confirmError }]} />}

            {/* Step 1: Set up an authenticator app */}
            {activeStep === 0 && (
              <div className="mfa-page__content-wrapper">
                <h1 className="govuk-heading-xl" id="mfa-setup-heading" tabIndex={-1}>
                  Set up an authenticator app
                </h1>

                <p className="govuk-body">
                  To keep your account secure, you need to use an authenticator app on your smartphone. You&rsquo;ll be asked to enter a 6-digit code from your authenticator app
                  each time you sign in.
                </p>

                <p className="govuk-body">Download the Microsoft Authenticator or Google Authenticator app on your phone.</p>

                <p className="govuk-body">Open the app and scan the QR code.</p>

                {error && <ErrorSummary heading="There is a problem" errors={[{ targetName: 'code-input', text: error }]} />}

                {setupData && (
                  <>
                    <div className="mfa-page__qr-wrapper">
                      <QRCodeSVG value={setupData.qrCodeUri} size={200} level="M" role="img" aria-label="QR code for setting up your authenticator app" />
                    </div>

                    <GovUKDetails summary="I cannot scan the QR code">
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

                    <Button onClick={() => setActiveStep(1)} data-testid="mfa-setup-step-0-continue">
                      Continue
                    </Button>
                  </>
                )}

                {!setupData && !error && (
                  <div className="mfa-page__loading">
                    <CircularProgress />
                  </div>
                )}
              </div>
            )}

            {/* Step 2: Check your authenticator app for a security code */}
            {activeStep === 1 && (
              <div className="mfa-page__content-wrapper">
                <h1 className="govuk-heading-xl" id="mfa-setup-heading" tabIndex={-1}>
                  Check your authenticator app for a security code
                </h1>

                <div className="mfa-page__input-wrapper">
                  <InputField
                    input={
                      {
                        id: 'code-input',
                        name: 'code',
                        'data-testid': 'mfa-setup-code-input',
                        value: code,
                        onChange: handleCodeChange,
                        onKeyPress: handleKeyPress,
                        disabled: isVerifying,
                        autoComplete: 'one-time-code',
                        inputMode: 'numeric',
                        pattern: '[0-9]*',
                        spellCheck: false,
                      } as React.InputHTMLAttributes<HTMLInputElement>
                    }
                    meta={{ error: error || undefined, touched: !!error }}
                    hint="This is a 6-digit code. For example 654321">
                    <strong>Authentication code</strong>
                  </InputField>
                </div>

                <GovUKButton onClick={() => handleVerify()} isLoading={isVerifying} data-testid="mfa-setup-verify-continue">
                  Continue
                </GovUKButton>

                <GovUKDetails summary="Help with signing in">
                  <p className="govuk-body">If you have problems signing in or getting a code, contact the DHSC support team.</p>
                  <p className="govuk-body">
                    Email:{' '}
                    <a className="govuk-link" href="mailto:gambling.levy@dhsc.gov.uk">
                      gambling.levy@dhsc.gov.uk
                    </a>
                  </p>
                </GovUKDetails>
              </div>
            )}

            {/* Step 3: Save your recovery codes */}
            {activeStep === 2 && recoveryCodes.length > 0 && (
              <div className="mfa-page__content-wrapper">
                <h1 className="govuk-heading-xl" id="mfa-setup-heading" tabIndex={-1}>
                  Save your recovery codes
                </h1>

                <p className="govuk-body">If you lose your smartphone or cannot use your authenticator app, you&rsquo;ll need a recovery code to sign in to your account.</p>

                <p className="govuk-body">Each code can only be used once. You should keep these codes safe.</p>

                <WarningText>You will not be able to see these codes again once you leave this screen.</WarningText>

                <h2 className="govuk-heading-l">Your recovery codes</h2>

                <div className="mfa-page__recovery-codes">
                  <ul className="mfa-page__recovery-codes-list">
                    {recoveryCodes.map((recoveryCode, index) => (
                      <li key={index} className="mfa-page__recovery-code">
                        {recoveryCode}
                      </li>
                    ))}
                  </ul>
                </div>

                <div className="mfa-page__button-group">
                  <Button buttonColour="#f3f2f1" buttonTextColour="#0b0c0c" onClick={handleCopyRecoveryCodes} data-testid="mfa-setup-recovery-copy">
                    Copy codes
                  </Button>
                  <Button buttonColour="#f3f2f1" buttonTextColour="#0b0c0c" onClick={handleDownloadRecoveryCodes} data-testid="mfa-setup-recovery-download">
                    Download codes
                  </Button>
                </div>

                <div className={`govuk-form-group ${confirmError ? 'govuk-form-group--error' : ''}`}>
                  {confirmError && (
                    <p id="confirm-saved-error" className="govuk-error-message">
                      <span className="govuk-visually-hidden">Error:</span> {confirmError}
                    </p>
                  )}
                  <div className="govuk-checkboxes govuk-checkboxes--small" data-module="govuk-checkboxes">
                    <div className="govuk-checkboxes__item">
                      <input
                        className="govuk-checkboxes__input"
                        id="confirm-saved"
                        name="confirm-saved"
                        data-testid="mfa-setup-confirm-saved"
                        type="checkbox"
                        checked={confirmSaved}
                        aria-describedby={confirmError ? 'confirm-saved-error' : undefined}
                        onChange={(ev) => {
                          setConfirmSaved(ev.target.checked);
                          if (ev.target.checked) setConfirmError(null);
                        }}
                      />
                      <label className="govuk-label govuk-checkboxes__label" htmlFor="confirm-saved">
                        I have saved my recovery codes in a secure place.
                      </label>
                    </div>
                  </div>
                </div>

                <GovUKButton onClick={handleFinish} data-testid="mfa-setup-finish">
                  Continue
                </GovUKButton>
              </div>
            )}
          </div>
        </div>
      </main>

      <LayoutFooter />
    </div>
  );
};

export default MfaSetup;
