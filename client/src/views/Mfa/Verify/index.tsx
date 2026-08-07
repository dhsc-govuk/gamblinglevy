import React, { useState, useCallback, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';

import usePageTitle from '../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../hooks/useErrorSummaryFocus';

import { InputField } from 'govuk-react';
import { ErrorSummary, GovUKButton, GovUKBackLink, GovUKDetails, H1 } from '../../../components/GovUKComponents';

import { useMfa } from '../../../components/MfaProvider';

import { useAuthProvider, roleToAreaMap } from '../../../components/AuthProvider';

import { getProfile } from '../../../components/AuthProvider/queries';

import { GovUKHeaderLogo } from '../../../components/Logos';

import LayoutFooter from '../../../layouts/General/LayoutFooter';
import { mfaTooManyRequestsMessage } from '../messages';

import { PageWrapper, Header, HeaderContainer, ServiceBanner, ServiceBannerContainer, ServiceName, MainContent, ContentContainer, ContentWrapper, InputWrapper } from './styles';

const baseUrl = import.meta.env.BASE_URL || '/';

const MfaVerify = (): React.ReactElement => {
  usePageTitle('Check your authenticator app for a security code');
  const navigate = useNavigate();

  const { userRole, signOut } = useAuthProvider();

  const { verify, setRequiresVerification } = useMfa();

  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const userRoleRef = useRef(userRole);

  useErrorSummaryFocus(submitAttempts, !!error);

  useEffect(() => {
    if (userRole) {
      userRoleRef.current = userRole;
    }
  }, [userRole]);

  const handleCodeChange = useCallback((e: React.ChangeEvent<HTMLInputElement>): void => {
    const value = e.target.value.replace(/\D/g, '').slice(0, 6);
    setCode(value);
    setError(null);
  }, []);

  const handleVerify = async (): Promise<void> => {
    setSubmitAttempts((n) => n + 1);

    if (code.length === 0) {
      setError('Enter the 6-digit code shown in your authenticator app');
      return;
    }

    if (code.length !== 6) {
      setError('The code must be 6 digits. Check the code and try again');
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

      const homeRoute = role ? `/${roleToAreaMap(role as any)}/home` : '/sign-in';

      navigate(redirectPath || homeRoute);
    } catch (err: any) {
      if (err?.response?.status === 401) {
        setError('That code no longer works. You need to enter the 6-digit code currently shown in your app');
      } else if (err?.response?.status === 429) {
        setError(mfaTooManyRequestsMessage(err));
      } else {
        setError('Verification failed. Try again.');
      }
    } finally {
      setIsLoading(false);
    }
  };

  const handleKeyPress = (e: React.KeyboardEvent): void => {
    if (e.key === 'Enter') {
      e.preventDefault();
      handleVerify();
    }
  };

  return (
    <PageWrapper data-testid="mfa-verify">
      <Header component="header" role="banner">
        <HeaderContainer>
          <a href={baseUrl} style={{ color: '#ffffff', textDecoration: 'none' }}>
            <GovUKHeaderLogo />
          </a>
        </HeaderContainer>
      </Header>

      <ServiceBanner component="section" aria-label="Service information">
        <ServiceBannerContainer>
          <ServiceName>Share progress preventing gambling harms</ServiceName>
        </ServiceBannerContainer>
      </ServiceBanner>

      <MainContent component="main" role="main">
        <ContentContainer>
          <GovUKBackLink onClick={signOut}>Back</GovUKBackLink>

          {error && <ErrorSummary testId="mfa-verify-error-summary" heading="There is a problem" errors={[{ targetName: 'code-input', text: error }]} />}

          <ContentWrapper>
            <H1>Check your authenticator app for a security code</H1>

            <InputWrapper>
              <InputField
                input={
                  {
                    id: 'code-input',
                    name: 'code',
                    'data-testid': 'mfa-verify-code-input',
                    value: code,
                    onChange: handleCodeChange,
                    onKeyPress: handleKeyPress,
                    disabled: isLoading,
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
            </InputWrapper>

            <GovUKButton onClick={handleVerify} isLoading={isLoading} data-testid="mfa-verify-continue">
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
          </ContentWrapper>
        </ContentContainer>
      </MainContent>

      <LayoutFooter />
    </PageWrapper>
  );
};

export default MfaVerify;
