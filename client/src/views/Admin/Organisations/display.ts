import type { LocalAuthorityDto } from './queries';

export type OrganisationStatus = LocalAuthorityDto['onboardingStatus'];

// The API calls a disabled organisation "suspended", while the admin journey uses "Deactivated".
export const ORGANISATION_STATUS_LABEL: Record<OrganisationStatus, string> = {
  invited: 'Invited',
  active: 'Active',
  suspended: 'Deactivated',
  deactivated: 'Deactivated',
};

export const displayOrganisationStatus = (status: OrganisationStatus): string => ORGANISATION_STATUS_LABEL[status];
