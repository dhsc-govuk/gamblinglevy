import React from 'react';
import { GovUKDetails, H2, ListItem, Paragraph, UnorderedList } from '../../GovUKComponents';

type AccessLevelGuidanceProps = {
  view: 'dhsc' | 'la';
  canAddUsers?: boolean;
  className?: string;
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

const AccessLevelGuidance = ({ view, canAddUsers = true, className }: AccessLevelGuidanceProps): React.ReactElement => {
  const headingId = `${view}-users-access-levels-heading`;
  const testId = view === 'dhsc' ? 'admin-users-list-access-level-guidance' : 'organisation-admin-users-list-access-level-guidance';
  const roles = view === 'dhsc' ? [descriptions.superAdmin, descriptions.laAdmin, descriptions.laLead] : [descriptions.laLead, descriptions.laAdmin];

  return (
    <section id="access-level-types" className={className} aria-labelledby={headingId} data-testid={testId}>
      <H2 id={headingId}>Access level types</H2>
      <Paragraph>
        {canAddUsers
          ? 'When you add a user, you’ll be asked to select their level of access to the service. This will determine what permissions they have.'
          : 'A user’s access level determines what permissions they have in this service.'}
      </Paragraph>
      <GovUKDetails summary="Types of access levels">
        <UnorderedList>
          {roles.map((role) => (
            <ListItem key={role.label}>
              <strong>{role.label}:</strong> {role.description}
            </ListItem>
          ))}
        </UnorderedList>
      </GovUKDetails>
    </section>
  );
};

export default AccessLevelGuidance;
