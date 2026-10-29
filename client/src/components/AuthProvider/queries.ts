import axios from 'axios';

export interface GetMeResponse {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  status: string;
  role: string;
  updatedAt: string;
  organisationId: string;
  organisationName: string | null;
  locationId: string;
  // Feature flags switched on in this environment (helpers/featureFlags.ts). Optional because DAC can deploy the
  // client ahead of an API that does not send it yet.
  features?: string[];
}

export interface MfaStatusResponse {
  isEnabled: boolean;
  enabledAt: string | null;
  recoveryCodesRemaining: number;
}

export const getProfile = () => axios.get<GetMeResponse>('profile/me');

export const getMfaStatus = () => axios.get<MfaStatusResponse>('mfa/status');
