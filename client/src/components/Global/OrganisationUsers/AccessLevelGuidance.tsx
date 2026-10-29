import React from 'react';
import { composeTestId } from '../../../helpers/testIds';
import { GovUKDetails, H2, ListItem, Paragraph, UnorderedList } from '../../GovUKComponents';

type AccessLevelGuidanceProps = {
  view: 'dhsc' | 'la';
  canAddUsers?: boolean;
  className?: string;
  // 'section' is the users-list page: a headed block of guidance. 'details' is the add/edit form, where the
  // guidance has to stay on the page — the link it replaces unmounted the form and discarded every answer
  // typed so far, which was most likely to happen to someone who opened it to ask where the LA roles went.
  variant?: 'section' | 'details';
  testId?: string;
};

const descriptions = {
  superAdmin: {
    label: 'Super Admin',
    description: 'can manage all users, organisations and data collections, with full access to the funding dashboard and audit logs.',
  },
  laAdmin: {
    label: 'LA Admin',
    description: 'can add activities, submit reports and manage user accounts for one local authority.',
  },
  laLead: {
    label: 'LA Lead',
    description: 'can add activities and submit reports.',
  },
};

// The DHSC forms gate the access levels on the Organisation field, so the guidance has to say so — otherwise it
// lists roles the dropdown is not currently offering with no explanation of where they went. Phrased as a
// statement rather than an instruction because the list page renders it too, where there is no field to act on.
const ORGANISATION_RULE = 'Super Admin is a DHSC-wide account and has no organisation. The two LA access levels each belong to one organisation.';

const AccessLevelGuidance = ({ view, canAddUsers = true, className, variant = 'section', testId }: AccessLevelGuidanceProps): React.ReactElement => {
  const headingId = `${view}-users-access-levels-heading`;
  // `view` is not a testid prefix — the contract's role segment is admin / organisation-admin, so both variants
  // derive their fallback from this rather than from `view` directly.
  const rolePrefix = view === 'dhsc' ? 'admin' : 'organisation-admin';
  const defaultTestId = variant === 'details' ? `${rolePrefix}-users-form-access-level-guidance` : `${rolePrefix}-users-list-access-level-guidance`;
  const roles = view === 'dhsc' ? [descriptions.superAdmin, descriptions.laAdmin, descriptions.laLead] : [descriptions.laLead, descriptions.laAdmin];

  const roleList = (
    <UnorderedList>
      {roles.map((role) => (
        <ListItem key={role.label}>
          <strong>{role.label}:</strong> {role.description}
        </ListItem>
      ))}
    </UnorderedList>
  );

  if (variant === 'details') {
    return (
      <GovUKDetails summary="Access level types" className={className} data-testid={testId ?? defaultTestId}>
        {roleList}
        {view === 'dhsc' && <Paragraph>{ORGANISATION_RULE}</Paragraph>}
      </GovUKDetails>
    );
  }

  // The id is kept deliberately, not left over: nothing links to it since the forms stopped navigating here,
  // but it remains a stable in-page target for anyone deep-linking to the guidance.
  return (
    <section id="access-level-types" className={className} aria-labelledby={headingId} data-testid={testId ?? defaultTestId}>
      <H2 id={headingId}>Access level types</H2>
      <Paragraph>
        {canAddUsers
          ? 'When you add a user, you’ll be asked to select their level of access to the service. This will determine what permissions they have.'
          : 'A user’s access level determines what permissions they have in this service.'}
      </Paragraph>
      <GovUKDetails summary="Types of access levels" data-testid={composeTestId(testId ?? defaultTestId, 'details')}>
        {roleList}
        {view === 'dhsc' && canAddUsers && <Paragraph>{ORGANISATION_RULE}</Paragraph>}
      </GovUKDetails>
    </section>
  );
};

export default AccessLevelGuidance;
