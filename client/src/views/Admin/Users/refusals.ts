import { EnumUserRole, ROLE_LABELS } from '../../../components/AuthProvider';

const SUPER_ADMIN = ROLE_LABELS[EnumUserRole.ADMIN];
const LA_ADMIN = ROLE_LABELS[EnumUserRole.ORGANISATION_ADMIN];

// The API answers a refusal with UserException.Message as a bare string, and those messages interpolate the user
// and organisation GUIDs, so these pages map a 422 to copy chosen from what they already know — never its body.
// A 422 from deactivate or delete is one of two permanent refusals: acting on your own account, or
// LastActiveAdminGuard keeping the last active Super Admin, or an organisation's last active LA Admin (BLUM-8751).
// "Try again" would send the admin round a loop that cannot succeed.
export const removalRefusalMessage = (action: 'deactivate' | 'delete', targetRole: string | undefined, isSelf: boolean): string => {
  if (isSelf) return `You cannot ${action} your own account.`;

  const outcome = action === 'deactivate' ? 'deactivated' : 'deleted';
  if (targetRole === EnumUserRole.ADMIN) {
    return `This user is the only active ${SUPER_ADMIN}, so their account cannot be ${outcome}. Add another ${SUPER_ADMIN} first.`;
  }
  if (targetRole === EnumUserRole.ORGANISATION_ADMIN) {
    return `This user is the only active ${LA_ADMIN} for their organisation, so their account cannot be ${outcome}. Add another ${LA_ADMIN} to that organisation first.`;
  }
  // The user had not loaded: say what was refused without guessing which kind of admin they are.
  return `This account cannot be ${outcome} because nobody else would be left to manage users. Add another admin first.`;
};

// The edit form's 422, scoped to the changes the handler actually refuses: name and email edits still go through,
// and saying everything was refused would leave the admin unable to tell which of their edits was the problem.
export const editRefusalMessage = (targetRole: string | undefined): string => {
  if (targetRole === EnumUserRole.ADMIN) {
    return `This user is the only active ${SUPER_ADMIN}, so you cannot change their access level. Add another ${SUPER_ADMIN} first — their name and email address can still be changed.`;
  }
  if (targetRole === EnumUserRole.ORGANISATION_ADMIN) {
    return `This user is the only active ${LA_ADMIN} for their organisation, so you cannot change their access level or move them to another organisation. Add another ${LA_ADMIN} to that organisation first — their name and email address can still be changed.`;
  }
  return 'You cannot change this user’s access level or organisation because nobody else would be left to manage users. Add another admin first — their name and email address can still be changed.';
};
