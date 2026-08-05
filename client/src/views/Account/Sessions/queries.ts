import axios from 'axios';

// mirrors api/src/Application/Mfa/Dtos/MfaSessionListItemDto.cs
export interface SessionDto {
  id: string;
  deviceLabel: string;
  ipAddress: string;
  createdAt: string;
  lastActivityAt: string;
  expiresAt: string;
  // Set by the API from the mfa_session cookie, not by anything the browser tells it — this is the one row
  // the user must not be encouraged to end by accident.
  isCurrent: boolean;
}

// Scoped server-side to the signed-in user; there is no way to ask for anybody else's (BLUM-7241).
export const getMySessions = () => axios.get<SessionDto[]>('profile/sessions');
