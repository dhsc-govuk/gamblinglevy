import { EnumUserRole, ROLE_LABELS, laAdminAssignableRoles } from '../../../components/AuthProvider';
import type { FilterOption } from '../../../components/GovUKComponents';

export const ROLE_ADMIN: string = EnumUserRole.ADMIN;

const SELECT_PROMPT = { value: '', label: 'Select access level' };

const laOptions = () => laAdminAssignableRoles.map((r) => ({ value: r, label: ROLE_LABELS[r] ?? r }));

const adminOption = () => ({ value: ROLE_ADMIN, label: ROLE_LABELS[EnumUserRole.ADMIN] });

// Access level and Organisation answer each other, so the dropdown is conditional on the organisation
// (BLUM-7452): Super Admin is system-wide and must have no organisation, while both LA roles belong to one.
// Offering all three regardless let a DHSC admin build a pair the API then rejects (LA role, no organisation)
// or silently discards (Super Admin with an organisation — AdminEditUserCommand drops the membership).
// Both lists still derive from laAdminAssignableRoles so they cannot disagree with the backend about the LA
// roles: Edit used to hardcode a shorter list than Create, which meant editing an LA Lead rendered an empty
// <select> and saving silently rewrote their role (BLUM-7038).
// `currentRole` keeps a role the conditional rule would not offer, because a native <select> cannot display a
// value absent from its option list — it renders as though nothing were chosen. A persisted user can legitimately
// hold that pair: DeleteLocalAuthorityCommand removes the OrganisationUser rows and leaves users.role alone, so
// an LA Lead of a deleted LA arrives as role='lead' with no organisation. Without this the DHSC admin repairing
// that account sees Access level blank with Super Admin as the only thing on offer, and taking it promotes them
// to DHSC-wide access. Keeping the role visible means validate() can ask for the missing organisation instead.
export const accessLevelOptionsFor = (organisationId: string, currentRole = ''): { value: string; label: string }[] => {
  const offered = organisationId ? [SELECT_PROMPT, ...laOptions()] : [SELECT_PROMPT, adminOption()];
  if (!currentRole || offered.some((option) => option.value === currentRole)) return offered;

  return [...offered, { value: currentRole, label: ROLE_LABELS[currentRole] ?? currentRole }];
};

// Whether a role already chosen is still offered for the given organisation state, so a form can clear a
// selection the user can no longer see. An unset role is "allowed" — there is nothing to clear. Deliberately
// asks accessLevelOptionsFor without a currentRole: passing one would make every role answer "allowed".
export const isRoleAllowedForOrganisation = (role: string, organisationId: string): boolean =>
  !role || accessLevelOptionsFor(organisationId).some((option) => option.value === role);

// Must ask about a *known* role: with the dropdown now conditional, "LA role and no organisation" is
// unreachable, so a bare `role !== ROLE_ADMIN` fired only when no role had been picked yet — telling the user
// to "Select an organisation" when the real problem was the empty access level, and steering them away from
// Super Admin, the one answer a blank organisation permits.
export const organisationRequiredFor = (role: string): boolean => !!role && role !== ROLE_ADMIN;

// Announced when changing the organisation discards the chosen access level, which is a status change a
// sighted user sees and a screen-reader user would not (WCAG 2.2 SC 4.1.3). Shared by both admin user forms
// so the two screens cannot end up telling the same story differently. Deliberately silent about *which*
// levels are now on offer: it has to read true whether the organisation was just set or just cleared.
// Deliberately stops short of "Select an access level": that is byte-identical to the field's own validation
// message, so once the notice became visible the two read as a stutter directly above one another.
export const ACCESS_LEVEL_CLEARED_NOTICE = 'Access level cleared. The available access levels have changed.';

// Every access level that can appear on the DHSC users list, ordered to match the `dhsc` list
// AccessLevelGuidance renders directly above the table so the filter and the guidance read the same way.
// Deliberately NOT laAdminAssignableRoles on its own: that is the set an LA *admin* may assign, so it omits
// Super Admin, who is a row on this list. Using it alone would leave every DHSC admin unfilterable and offer
// two of the three levels the guidance describes. Spreading it after ADMIN keeps the two LA roles sourced
// from the one list the repo treats as canonical, so the filter cannot drift from the backend role set.
export const accessLevelFilterOptions: FilterOption[] = [EnumUserRole.ADMIN, ...laAdminAssignableRoles].map((role) => ({
  value: role,
  label: ROLE_LABELS[role] ?? role,
}));
