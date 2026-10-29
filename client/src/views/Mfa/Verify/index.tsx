import React, { useState, useCallback, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';

import usePageTitle from '../../../hooks/usePageTitle';

import { ErrorSummary, GovUKButton, GovUKBackLink, GovUKFieldset, H1 } from '../../../components/GovUKComponents';

import SigningInHelp from '../../../components/Global/Mfa/SigningInHelp';

import { useMfa } from '../../../components/MfaProvider';

import { useAuthProvider, roleToAreaMap, type EnumUserRole } from '../../../components/AuthProvider';

import { getProfile } from '../../../components/AuthProvider/queries';

import SignInJourneyLayout from '../../../layouts/SignInJourney';
import { mfaCodeError, mfaCodeRefusalMessage, sanitiseMfaCode } from '../messages';
import '../styles.css';

const PAGE_TITLE = 'Check your authenticator app for a security code';

const MfaVerify = (): React.ReactElement => {
  const navigate = useNavigate();

  const { userRole, signOut } = useAuthProvider();

  const { verify, setRequiresVerification } = useMfa();

  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(error ? `Error: ${PAGE_TITLE}` : PAGE_TITLE);

  const userRoleRef = useRef(userRole);

  useEffect(() => {
    if (userRole) {
      userRoleRef.current = userRole;
    }
  }, [userRole]);

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

    setIsLoading(true);
    setError(null);

    try {
      await verify(code);

      setRequiresVerification(false);

      const redirectPath = sessionStorage.getItem('mfa_redirect_path');
      sessionStorage.removeItem('mfa_redirect_path');

      let role: string | null = userRoleRef.current || userRole;

      if (!role) {
        try {
          const profileResponse = await getProfile();
          role = profileResponse.data.role;
        } catch {
          // If profile fetch fails, navigate to sign-in
        }
      }

      const homeRoute = role ? `/${roleToAreaMap(role as EnumUserRole)}/home` : '/sign-in';

      navigate(redirectPath || homeRoute);
    } catch (err) {
      setError(mfaCodeRefusalMessage(err, 'Verification failed. Try again.'));
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <SignInJourneyLayout testId="mfa-verify">
      <div className="govuk-main-wrapper">
        <GovUKBackLink onClick={signOut} data-testid="mfa-verify-back">
          Back
        </GovUKBackLink>

        <ErrorSummary
          testId="mfa-verify-error-summary"
          heading="There is a problem"
          errors={error ? [{ targetName: 'code-input', text: error }] : []}
          focusTrigger={submitAttempts}
        />

        <div className="mfa-page__content-wrapper">
          <H1>{PAGE_TITLE}</H1>

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
              disabled={isLoading}
              autoComplete="one-time-code"
              inputMode="numeric"
              spellCheck={false}
              testId="mfa-verify-code-input"
            />

            <GovUKButton type="submit" isLoading={isLoading} data-testid="mfa-verify-continue">
              Continue
            </GovUKButton>
          </form>

          <SigningInHelp testId="mfa-verify-help" />
        </div>
      </div>
    </SignInJourneyLayout>
  );
};

export default MfaVerify;
