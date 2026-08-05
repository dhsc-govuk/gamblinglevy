import React from 'react';
import { Outlet } from 'react-router-dom';
import PageLoading from '../PageLoading';

interface ProtectedRoleRouteProps {
  fallbackComponent: React.ReactElement;
  allowedRoles: string[];
  userRole: string | null;
  // When true, show a loading screen instead of evaluating the role. Use this
  // while auth/profile is still resolving so we don't flash the 403 fallback.
  isLoading?: boolean;
}

const ProtectedRoleRoute = ({ fallbackComponent, allowedRoles, userRole, isLoading = false }: ProtectedRoleRouteProps): React.ReactElement => {
  if (isLoading) {
    // Delay rendering so quick auth resolves never paint the main loader at
    // all, and slow resolves still overshadow any in-page loader cleanly.
    return <PageLoading testId="protected-route-loading" delayMs={500} />;
  }
  if (!userRole || !allowedRoles.includes(userRole)) {
    return fallbackComponent;
  }
  return <Outlet />;
};

export default ProtectedRoleRoute;
