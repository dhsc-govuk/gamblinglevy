import { EnumUserRole, ROLE_LABELS, laAdminAssignableRoles } from '../../../components/AuthProvider';

export const ROLE_ADMIN: string = EnumUserRole.ADMIN;

const SELECT_PROMPT = { value: '', label: 'Select access level' };

const laOptions = () => laAdminAssignableRoles.map((r) => ({ value: r, label: ROLE_LABELS[r] ?? r }));

const adminOption = () => ({ value: ROLE_ADMIN, label: ROLE_LABELS[EnumUserRole.ADMIN] });

// A DHSC admin can assign every live role. Whether an organisation is required is validated separately
// because Super Admin is system-wide while both LA roles belong to an organisation.
export const createAccessLevelOptions = (): { value: string; label: string }[] => [SELECT_PROMPT, adminOption(), ...laOptions()];

// Edit uses the same complete role set as Create. Both lists derive from laAdminAssignableRoles so they
// cannot disagree about the LA roles: Edit used to hardcode a shorter list than Create, which meant
// editing an LA Lead rendered an empty <select> and saving silently rewrote their role (BLUM-7038).
export const editAccessLevelOptions = (): { value: string; label: string }[] => [SELECT_PROMPT, adminOption(), ...laOptions()];
