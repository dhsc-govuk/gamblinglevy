import axios from 'axios';

export interface LocalAuthorityDto {
  id: string;
  name: string;
  onsCode: string | null;
  region: string;
  type: 'LA' | 'VCSE';
  eCode: string;
  onboardingStatus: 'invited' | 'active' | 'suspended' | 'deactivated';
  allocatedAmount: number | null;
  fundingFinancialYear: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export const getLocalAuthorities = (onboardingStatus?: string) =>
  axios.get<LocalAuthorityDto[]>('organisations/local-authorities', {
    params: onboardingStatus ? { onboardingStatus } : undefined,
  });

export const getLocalAuthority = (organisationId: string) => axios.get<LocalAuthorityDto>(`organisations/local-authorities/${organisationId}`);

// Mirrors Organisation.ECodeMaxLength on the API, which backs the varchar(64) column. Checked client-side so an
// over-long code is caught on the details step next to the field, rather than surviving to the final submit and
// coming back as a server error the review page can only attach to the Save button.
export const E_CODE_MAX_LENGTH = 64;

export const REGIONS: string[] = [
  'East Midlands',
  'East of England',
  'London',
  'North East',
  'North West',
  'South East',
  'South West',
  'West Midlands',
  'Yorkshire and the Humber',
];
