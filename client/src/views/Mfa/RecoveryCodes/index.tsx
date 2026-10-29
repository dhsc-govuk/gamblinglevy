import React, { useEffect, useLayoutEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { ROUTES } from '../../../helpers/routes';
import scrollToTop from '../../../helpers/scrollToTop';
import usePageTitle from '../../../hooks/usePageTitle';
import { ErrorSummary, GovUKButton, GovUKFieldset, GovUKWarningText, PageLoading } from '../../../components/GovUKComponents';
import { useMfa } from '../../../components/MfaProvider';
import { useAuthProvider, roleToAreaMap } from '../../../components/AuthProvider';
import SignInJourneyLayout from '../../../layouts/SignInJourney';
import { mfaCodeError, mfaCodeRefusalMessage, sanitiseMfaCode } from '../messages';
import RecoveryCodesList from '../../../components/Global/Mfa/RecoveryCodesList';
import '../styles.css';

const FORM_TITLE = 'Generate new recovery codes';
const CODES_TITLE = 'Save your new recovery codes';
const NOT_ENABLED_TITLE = 'You have not set up an authenticator app';

const MfaRecoveryCodes = (): React.ReactElement => {
  const navigate = useNavigate();
  const { inProgress, accounts } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const { userRole } = useAuthProvider();
  const { recoveryCodes, regenerateCodes, clearRecoveryCodes, isEnabled, isLoading: isStatusLoading } = useMfa();

  const isMsalLoading = inProgress !== InteractionStatus.None || (isAuthenticated && accounts.length === 0);
  // Until MSAL has settled and the status query has answered, isEnabled reads false: deciding on it earlier showed
  // "not enabled" for a moment to everyone who had set one up.
  const isChecking = isMsalLoading || !isAuthenticated || isStatusLoading;

  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isRegenerating, setIsRegenerating] = useState(false);
  const [submitAttempts, setSubmitAttempts] = useState(0);
  // Codes still held from an unfinished setup are shown straight away rather than replaced unseen.
  const [codesVisible, setCodesVisible] = useState(recoveryCodes.length > 0);

  const showCodes = codesVisible && recoveryCodes.length > 0;
  const title = !isEnabled ? NOT_ENABLED_TITLE : showCodes ? CODES_TITLE : FORM_TITLE;
  usePageTitle(isChecking ? undefined : error ? `Error: ${title}` : title);

  // The page is reached directly or from a link, and AuthProvider skips the profile on /mfa pages, so the role
  // can still be unknown here; the service root then routes the user on.
  const homeRoute = userRole ? `/${roleToAreaMap(userRole)}/home` : ROUTES.root;

  useEffect(() => {
    if (!isMsalLoading && !isAuthenticated) {
      navigate(ROUTES.public.signIn);
    }
  }, [isMsalLoading, isAuthenticated, navigate]);

  // The new codes replace the form in place, with no route change, so focus moves to the new heading rather than
  // staying on a submit button that no longer exists.
  useLayoutEffect(() => {
    if (showCodes) {
      scrollToTop();
      document.getElementById('mfa-recovery-codes-heading')?.focus({ preventScroll: true });
    }
  }, [showCodes]);

  const handleCodeChange = (e: React.ChangeEvent<HTMLInputElement>): void => {
    setCode(sanitiseMfaCode(e.target.value));
    setError(null);
  };

  const handleRegenerate = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);

    const codeError = mfaCodeError(code);
    if (codeError) {
      setError(codeError);
      return;
    }

    setIsRegenerating(true);
    setError(null);

    try {
      await regenerateCodes(code);
      setCode('');
      setCodesVisible(true);
    } catch (err) {
      setError(
        mfaCodeRefusalMessage(
          err,
          'We could not generate new recovery codes. Try again.',
          'You have not set up an authenticator app. Set one up before you generate recovery codes.'
        )
      );
    } finally {
      setIsRegenerating(false);
    }
  };

  const handleFinish = (): void => {
    clearRecoveryCodes();
    navigate(homeRoute);
  };

  if (isChecking) {
    return (
      <SignInJourneyLayout testId="mfa-recovery-codes-loading" headerTestId="mfa-recovery-codes-header">
        <PageLoading testId="mfa-recovery-codes-loading-status" />
      </SignInJourneyLayout>
    );
  }

  if (!isEnabled) {
    return (
      <SignInJourneyLayout testId="mfa-recovery-codes-not-enabled" headerTestId="mfa-recovery-codes-header">
        <div className="govuk-main-wrapper">
          <div className="mfa-page__content-wrapper">
            <h1 className="govuk-heading-xl">{NOT_ENABLED_TITLE}</h1>
            <GovUKWarningText data-testid="mfa-recovery-codes-not-enabled-warning">You need an authenticator app before you can generate recovery codes.</GovUKWarningText>
            <GovUKButton onClick={() => navigate(ROUTES.mfa.setup)} data-testid="mfa-recovery-codes-go-to-setup">
              Set up an authenticator app
            </GovUKButton>
          </div>
        </div>
      </SignInJourneyLayout>
    );
  }

  return (
    <SignInJourneyLayout testId="mfa-recovery-codes" headerTestId="mfa-recovery-codes-header">
      <div className="govuk-main-wrapper">
        {/* Always mounted, so it exists before an error arrives to be focused for it (see GovUKErrorSummary). */}
        <ErrorSummary
          testId="mfa-recovery-codes-error-summary"
          heading="There is a problem"
          errors={!showCodes && error ? [{ targetName: 'code-input', text: error }] : []}
          focusTrigger={submitAttempts}
        />

        <div className="mfa-page__content-wrapper">
          {showCodes ? (
            <>
              <h1 className="govuk-heading-xl mfa-page__focus-target" id="mfa-recovery-codes-heading" tabIndex={-1}>
                {CODES_TITLE}
              </h1>

              <p className="govuk-body">Your old recovery codes no longer work. Each new code can only be used once. You should keep these codes safe.</p>

              <GovUKWarningText data-testid="mfa-recovery-codes-warning">You will not be able to see these codes again once you leave this screen.</GovUKWarningText>

              <h2 className="govuk-heading-l">Your recovery codes</h2>

              <RecoveryCodesList codes={recoveryCodes} testId="mfa-recovery-codes-list" />

              <GovUKButton onClick={handleFinish} data-testid="mfa-recovery-codes-finish">
                Continue
              </GovUKButton>
            </>
          ) : (
            <>
              <h1 className="govuk-heading-xl">{FORM_TITLE}</h1>

              <p className="govuk-body">Your current recovery codes will stop working when you generate new ones.</p>

              {/* A form, so Enter in the code field submits it like the button does. */}
              <form noValidate onSubmit={handleRegenerate}>
                <GovUKFieldset.Input
                  id="code-input"
                  name="code"
                  label="Security code"
                  hint="Enter the 6-digit code shown in your authenticator app, for example 654321"
                  value={code}
                  onChange={handleCodeChange}
                  error={error ?? undefined}
                  width="5"
                  disabled={isRegenerating}
                  autoComplete="one-time-code"
                  inputMode="numeric"
                  spellCheck={false}
                  testId="mfa-recovery-codes-code"
                />

                <div className="govuk-button-group">
                  <GovUKButton type="submit" isLoading={isRegenerating} data-testid="mfa-recovery-codes-regenerate">
                    Generate new codes
                  </GovUKButton>
                  <Link className="govuk-link" to={homeRoute} data-testid="mfa-recovery-codes-cancel">
                    Cancel
                  </Link>
                </div>
              </form>
            </>
          )}
        </div>
      </div>
    </SignInJourneyLayout>
  );
};

export default MfaRecoveryCodes;
