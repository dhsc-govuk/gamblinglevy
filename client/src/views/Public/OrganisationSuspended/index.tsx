import React from 'react';
import PublicMessageLayout from '../../../layouts/PublicMessage';

// Where the ORGANISATION_SUSPENDED envelope lands (BLUM-6211).
const OrganisationSuspended = (): React.ReactElement => (
  <PublicMessageLayout
    title="Your organisation’s access has been suspended"
    paragraphs={[
      'Your organisation’s access to Share progress preventing gambling harms has been suspended by DHSC. You cannot sign in to the service while your organisation is suspended.',
      'If you think this is a mistake, contact DHSC.',
    ]}
    testId="public-organisation-suspended"
  />
);

export default OrganisationSuspended;
