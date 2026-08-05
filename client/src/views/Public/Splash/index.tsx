import React, { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { styled } from '@mui/material/styles';
import { Fade } from '@mui/material';
import { Logo } from '../../../components/Logos';
import { roleToAreaMap, useAuthProvider } from '../../../components/AuthProvider';

const Root = styled('div')(({ theme }) => ({
  height: '100%',
  width: '100%',
  backgroundColor: theme.palette.background.paper,
  display: 'flex',
  justifyContent: 'center',
  alignItems: 'center',
}));

const Icon = styled('div')(() => ({
  display: 'block',
  '& svg': {
    width: '28rem',
    height: '28rem',
    flex: '0 0 auto',
    padding: '0 3rem',
  },
}));

const LoadingText = styled('div')({
  fontSize: '24px',
  fontWeight: 500,
  color: '#333',
});

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
    <Root data-testid="public-splash">
      <Fade in timeout={2700}>
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <Icon>
            <Logo />
          </Icon>
          <LoadingText>Loading Share progress preventing gambling harms...</LoadingText>
        </div>
      </Fade>
    </Root>
  );
};

export default Splash;
