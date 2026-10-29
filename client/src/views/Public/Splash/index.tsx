import React, { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { Logo } from '../../../components/Logos';
import { roleToAreaMap, useAuthProvider } from '../../../components/AuthProvider';
import './styles.css';

const Splash = (): React.ReactElement => {
  const navigate = useNavigate();
  const isAuthenticated = useIsAuthenticated();
  const { inProgress } = useMsal();
  const { userRole } = useAuthProvider();

  useEffect(() => {
    if (userRole) {
      navigate(`/${roleToAreaMap(userRole)}/home`, { replace: true });
    }
  }, [userRole, navigate]);

  useEffect(() => {
    if (isAuthenticated) {
      return undefined;
    }
    // MSAL's `inProgress` normally settles to `None` quickly, but a stuck/interrupted redirect
    // interaction (e.g. stale sessionStorage state from a previous failed sign-in) can leave it
    // stuck on another status forever. Without a fallback here, the splash screen would never
    // navigate away and the app would appear to hang on "/" indefinitely.
    const delay = inProgress === InteractionStatus.None ? 1500 : 5000;
    const timer = setTimeout(() => {
      navigate('/sign-in', { replace: true });
    }, delay);
    return () => clearTimeout(timer);
  }, [inProgress, isAuthenticated, navigate]);

  return (
    <div className="app-splash" data-testid="public-splash">
      <div className="app-splash__content">
        <div className="app-splash__logo">
          <Logo />
        </div>
        <p className="govuk-body-l govuk-!-margin-bottom-0">Loading Share progress preventing gambling harms...</p>
      </div>
    </div>
  );
};

export default Splash;
