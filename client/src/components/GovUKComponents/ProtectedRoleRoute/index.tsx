import React, { useEffect } from 'react';
import { Outlet } from 'react-router-dom';
import PageLoading from '../PageLoading';

interface ProtectedRoleRouteProps {
  fallbackComponent: React.ReactElement;
  allowedRoles: string[];
  userRole: string | null;
  // When true, show a loading screen instead of evaluating the role. Use this
  // while auth/profile is still resolving so we don't flash the 403 fallback.
  isLoading?: boolean;
  // Starts downloading the code for this group's views (BLUM-8759). Without it the download begins only once
  // the role is known and the view renders, so a deep link waits for the profile and then for the chunk; with
  // it the two run side by side. That means it runs before the role is known: a user who opens a URL they are
  // not allowed to see downloads the views and is then refused. Once the role is known it is not called for one
  // that will be refused.
  preload?: () => void;
}

const ProtectedRoleRoute = ({ fallbackComponent, allowedRoles, userRole, isLoading = false, preload }: ProtectedRoleRouteProps): React.ReactElement => {
  const isAllowed = !!userRole && allowedRoles.includes(userRole);

  useEffect(() => {
    if (isLoading || isAllowed) preload?.();
  }, [isLoading, isAllowed, preload]);

  if (isLoading) {
    // Delay rendering so quick auth resolves never paint the main loader at
    // all, and slow resolves still overshadow any in-page loader cleanly.
    return <PageLoading testId="protected-route-loading" delayMs={500} />;
  }
  if (!isAllowed) {
    return fallbackComponent;
  }
  return <Outlet />;
};

export default ProtectedRoleRoute;
