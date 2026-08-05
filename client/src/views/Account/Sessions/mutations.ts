import axios from 'axios';

// Ends one of the caller's own sessions (BLUM-7241 AC4). The server revokes the record, so the effect is
// immediate — the other browser is refused on its very next request rather than when some token expires.
// A session id that is not the caller's own comes back 404, exactly as an unknown one does.
export const revokeSession = (sessionId: string) => axios.post(`profile/sessions/${sessionId}/revoke`);
