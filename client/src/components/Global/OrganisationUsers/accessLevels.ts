import { EnumUserRole, laAdminAssignableRoles, ROLE_LABELS } from '../../AuthProvider';
import type { FilterOption } from '../../GovUKComponents';

// AccessLevelGuidance renders its `la` view as LA Lead then LA Admin — the reverse of the DHSC view — and it
// sits immediately above this filter on the organisation users list. The sibling rule in
// views/Admin/Users/accessLevels.ts is that the filter and the guidance directly above it read the same way,
// so the LA filter follows the LA guidance rather than laAdminAssignableRoles' own order.
const LA_GUIDANCE_ORDER: EnumUserRole[] = [EnumUserRole.LEAD, EnumUserRole.ORGANISATION_ADMIN];

// Ordered by the guidance, but the SET still comes from laAdminAssignableRoles — the single list every role
// dropdown uses — with anything it gains appended rather than silently dropped. Ordering alone must never be
// able to shrink the filter.
const orderedLaRoles: EnumUserRole[] = [
  ...LA_GUIDANCE_ORDER.filter((role) => laAdminAssignableRoles.includes(role)),
  ...laAdminAssignableRoles.filter((role) => !LA_GUIDANCE_ORDER.includes(role)),
];

// The access levels that can appear on an organisation-scoped user list. Deliberately excludes Super Admin:
// that is a DHSC-wide account with no organisation, so it is never a row here — which is why this cannot
// simply reuse views/Admin/Users/accessLevels' accessLevelFilterOptions, whose list leads with it.
export const laAccessLevelFilterOptions: FilterOption[] = orderedLaRoles.map((role) => ({
  value: role,
  label: ROLE_LABELS[role] ?? role,
}));
