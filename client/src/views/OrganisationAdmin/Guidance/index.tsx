import React from 'react';
import { GeneralLayout } from '../../../layouts';
import { GuidancePage } from '../../../components/Global/Guidance';
import { homeLinkForRole, useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { laGuidanceSections } from './sections';

const PAGE_TITLE = 'Guidance';

// LA guidance (BLUM-7633). Reached from the "Guidance" tab in the header service navigation, which is only
// rendered for the two LA roles, and from the "Support and guidance" link on the LA home page. The route is
// gated to laRoles in Routes.tsx so a DHSC user cannot reach this copy — they have their own page at
// /admin/guidance.
//
// GeneralLayout, not OrganisationManagementLayout: Guidance is a top-level tab, and that layout's side
// navigation is the Organisation/Users account area, which this page does not belong to.
const Guidance = (): React.ReactElement => {
  usePageTitle(PAGE_TITLE);
  const { userRole } = useAuthProvider();

  return (
    <GeneralLayout breadcrumbs={[{ label: 'Home', link: homeLinkForRole(userRole) }]}>
      <div className="govuk-grid-row">
        <div className="govuk-grid-column-two-thirds">
          <GuidancePage sections={laGuidanceSections} testId="organisation-admin-guidance" />
        </div>
      </div>
    </GeneralLayout>
  );
};

export default Guidance;
