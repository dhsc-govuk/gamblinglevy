import React, { useState } from 'react';
import { ACCESS_LEVEL_CLEARED_NOTICE, isRoleAllowedForOrganisation, organisationRequiredFor } from './accessLevels';

// The two keys of both admin user forms' FormErrors that this gesture can resolve.
export interface AccessLevelClampErrors {
  role?: string;
  organisationId?: string;
}

interface UseAccessLevelClampParams<TErrors extends AccessLevelClampErrors> {
  role: string;
  setRole: (role: string) => void;
  setOrganisationId: (organisationId: string) => void;
  setErrors: React.Dispatch<React.SetStateAction<TErrors>>;
}

interface AccessLevelClamp {
  accessLevelNotice: string;
  handleOrganisationChange: (nextOrganisationId: string) => void;
  handleRoleChange: (nextRole: string) => void;
}

// Delete the key rather than set it undefined: usePageTitle counts keys, so an
// undefined value leaves the "Error: " title prefix on a page that no longer has an error.
const withoutError = <TErrors extends AccessLevelClampErrors>(prev: TErrors, key: keyof AccessLevelClampErrors): TErrors => {
  if (prev[key] === undefined) return prev;

  // Widened to the shared shape so `delete` sees the optional declaration, then restored to the caller's type.
  const next: AccessLevelClampErrors = { ...prev };
  delete next[key];
  return next as TErrors;
};

/**
 * An error is cleared when the user *answers* the field, never when this code empties it. Clearing on the way
 * out hid "Select an access level" at the exact moment it became true again, so a form that could not be
 * submitted presented as complete.
 *
 * These two rules are exported as pure functions so they can be executed without rendering the hook.
 */
export const errorsAfterOrganisationChange = <TErrors extends AccessLevelClampErrors>(prev: TErrors, nextOrganisationId: string): TErrors =>
  nextOrganisationId ? withoutError(prev, 'organisationId') : prev;

export const errorsAfterRoleChange = <TErrors extends AccessLevelClampErrors>(prev: TErrors, nextRole: string): TErrors => {
  if (!nextRole) return prev;

  const answered = withoutError(prev, 'role');
  // Super Admin needs no organisation, so an outstanding "Select an organisation" stops being true too.
  return organisationRequiredFor(nextRole) ? answered : withoutError(answered, 'organisationId');
};

/**
 * Access level and Organisation answer each other, so choosing one can invalidate the other. Both admin user
 * forms need identical behaviour, and this file exists rather than a copy in each view because two hand-kept
 * copies drifting apart is the defect this area already shipped once (BLUM-7038).
 */
export function useAccessLevelClamp<TErrors extends AccessLevelClampErrors>({ role, setRole, setOrganisationId, setErrors }: UseAccessLevelClampParams<TErrors>): AccessLevelClamp {
  const [accessLevelNotice, setAccessLevelNotice] = useState('');

  const handleOrganisationChange = (nextOrganisationId: string) => {
    setOrganisationId(nextOrganisationId);
    setErrors((prev) => errorsAfterOrganisationChange(prev, nextOrganisationId));
    if (isRoleAllowedForOrganisation(role, nextOrganisationId)) return;

    // The chosen access level is no longer on offer, so it is discarded. errors.role is deliberately left
    // alone: the role is empty again, so "Select an access level" is true again.
    setRole('');
    setAccessLevelNotice(ACCESS_LEVEL_CLEARED_NOTICE);
  };

  const handleRoleChange = (nextRole: string) => {
    setRole(nextRole);
    // Blanking the notice is what lets a later clamp announce at all: re-setting the same string mutates no
    // DOM, so every clamp has to be a genuine '' → message transition. That holds because the pair can only go
    // out of step here or in handleOrganisationChange — nothing else writes `role` once the form is hydrated
    // (Edit hydrates once, by ref), and after a clamp `role` is '', which isRoleAllowedForOrganisation always
    // permits, so a second clamp cannot happen without an intervening call to this handler.
    setAccessLevelNotice('');
    setErrors((prev) => errorsAfterRoleChange(prev, nextRole));
  };

  return { accessLevelNotice, handleOrganisationChange, handleRoleChange };
}

export default useAccessLevelClamp;
