import { EnumUserRole, ROLE_LABELS } from '../../../components/AuthProvider';

export const JOB_ROLE_OTHER = 'other';

// Labels come from ROLE_LABELS so the dropdown can never drift from the product's role vocabulary again (BLUM-7043).
export const JOB_ROLE_OPTIONS = [
  { value: '', label: 'Select a job role' },
  { value: EnumUserRole.ORGANISATION_ADMIN, label: ROLE_LABELS[EnumUserRole.ORGANISATION_ADMIN] },
  { value: JOB_ROLE_OTHER, label: 'Other' },
];

export interface ContactJobRoleFields {
  jobRole: string;
  jobRoleTitle: string;
  // The role the contact already has on the server. Absent for contacts added in the form.
  originalRole?: string;
}

export const splitFullName = (fullName: string): { firstName: string; lastName: string } => {
  const parts = fullName.trim().split(/\s+/);
  const [firstName = '', ...rest] = parts;
  return { firstName, lastName: rest.join(' ') };
};

// "Other" is a job-title escape hatch, not an access level. Keep the role a contact loaded from the API
// already has; a brand-new contact gets the least-privilege role, since picking "Other" expresses no
// intent to grant admin — only the explicit "LA admin" option does that.
export const roleForContact = (contact: ContactJobRoleFields): string => (contact.jobRole === JOB_ROLE_OTHER ? contact.originalRole ?? EnumUserRole.LEAD : contact.jobRole);

// Check your answers must state the access level being granted, which the "Other" title alone doesn't convey.
export const displayContactJobRole = (contact: ContactJobRoleFields): string => {
  const role = roleForContact(contact);
  const roleLabel = ROLE_LABELS[role] ?? role;
  if (contact.jobRole !== JOB_ROLE_OTHER) return roleLabel;

  // On Edit the title is seeded from the role's own label — don't render "LA admin (LA admin)".
  // Compared case-insensitively: the seeded title is often re-typed with different capitalisation.
  const title = contact.jobRoleTitle.trim();
  return title && title.toLowerCase() !== roleLabel.toLowerCase() ? `${title} (${roleLabel})` : roleLabel;
};
