import React from 'react';
import { GeneralLayout } from '../../../layouts';
import { GuidancePage } from '../../../components/Global/Guidance';
import { ROUTES } from '../../../helpers/routes';
import usePageTitle from '../../../hooks/usePageTitle';
import { dhscGuidanceSections } from './sections';

const PAGE_TITLE = 'Guidance';

// DHSC guidance (BLUM-7633). Reached from the "Guidance" tab in the header service navigation, which for
// DHSC replaced the old Management side-nav item in BLUM-7819. The route is gated to adminRoles in
// Routes.tsx, so an LA user never sees this copy — they have their own page at
// /organisation-admin/guidance, written for their role.
//
// GeneralLayout, not ManagementLayout: Guidance is a top-level tab and no longer part of the Management
// section, so neither that side navigation nor a "Management" breadcrumb belongs on it.
const Guidance = (): React.ReactElement => {
  usePageTitle(PAGE_TITLE);

  return (
    <GeneralLayout breadcrumbs={[{ label: 'Home', link: ROUTES.admin.home }]}>
      <div className="govuk-grid-row">
        <div className="govuk-grid-column-two-thirds">
          <GuidancePage sections={dhscGuidanceSections} testId="admin-guidance" />
        </div>
      </div>
    </GeneralLayout>
  );
};

export default Guidance;
