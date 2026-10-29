import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { H1, Paragraph } from '../../../components/GovUKComponents';
import { GeneralLayout } from '../../../layouts';
import usePageTitle from '../../../hooks/usePageTitle';
import { ROUTES } from '../../../helpers/routes';

interface Props {
  title: string;
  message: string;
}

const UpcomingFeature = ({ title, message }: Props): React.ReactElement => {
  usePageTitle(title);

  return (
    <GeneralLayout breadcrumbs={[{ label: 'Home', link: ROUTES.organisationAdmin.home }]}>
      <div className="govuk-grid-row">
        <div className="govuk-grid-column-two-thirds" data-testid="organisation-admin-upcoming-feature">
          <H1>{title}</H1>
          <Paragraph>{message}</Paragraph>
          <RouterLink to={ROUTES.organisationAdmin.home} className="govuk-link" data-testid="organisation-admin-upcoming-feature-return-home">
            Return to home
          </RouterLink>
        </div>
      </div>
    </GeneralLayout>
  );
};

export default UpcomingFeature;
