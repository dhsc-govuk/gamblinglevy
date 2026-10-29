import React from 'react';
import PublicMessageLayout from '../../../layouts/PublicMessage';

// Where the USER_DEACTIVATED envelope lands (BLUM-6386). Deliberately a page rather than a banner on the
// sign-in screen: the user has authenticated with Microsoft successfully and there is nothing for them to
// retry, so offering them the sign-in form again would invite exactly the loop the ticket describes.
//
// The copy names the account's state and stops there (AC4). It cannot be more specific about who to
// contact either: a deactivated user carries no role or organisation claim, so this page genuinely does
// not know whether they belong to DHSC or to a local authority.
const AccountDeactivated = (): React.ReactElement => (
  <PublicMessageLayout
    title="Your account has been deactivated"
    paragraphs={[
      'Your account for Share progress preventing gambling harms has been deactivated, so you cannot use the service.',
      'If you think this is a mistake, contact your organisation’s administrator or DHSC.',
    ]}
    testId="public-account-deactivated"
  />
);

export default AccountDeactivated;
