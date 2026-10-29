import React from 'react';
import { Outlet } from 'react-router-dom';
import { FeatureFlag, isFeatureEnabled } from '../../../helpers/featureFlags';

interface FeatureRouteProps {
  feature: FeatureFlag;
  // The enabled flag names, from useAuthProvider().features.
  features: readonly string[];
  // What an unreleased route shows instead — NotFound, so for a user whose role reaches this group it reads exactly
  // like a route that does not exist, matching the API's 404 for the same endpoints.
  fallbackComponent: React.ReactElement;
}

// A layout route that renders its child routes only while `feature` is switched on in this environment. Nest it
// inside a ProtectedRoleRoute group rather than outside one: that group has already waited for the profile, which
// is where the flags come from, so an enabled route never flashes the fallback while it loads. The cost is that a user
// whose role does not reach the group sees that group's NotAuthorised instead; a flag that must answer every role
// identically should leave its route out of the router altogether, as the forms builder does (BLUM-7464).
const FeatureRoute = ({ feature, features, fallbackComponent }: FeatureRouteProps): React.ReactElement => (isFeatureEnabled(features, feature) ? <Outlet /> : fallbackComponent);

export default FeatureRoute;
