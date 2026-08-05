import React, { useEffect } from 'react';
import { Link as RouterLink, useNavigate, useLocation, useParams } from 'react-router-dom';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, H1, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { ROUTES } from '../../../../../helpers/routes';
import type { EditOrganisationContactsState } from '../Contacts';

const LocalAuthoritiesEditDiscard = (): React.ReactElement => {
  usePageTitle('Discard edits');
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const location = useLocation();

  const state = location.state as EditOrganisationContactsState | null;
  const name = state?.organisation.name || 'this organisation';

  useEffect(() => {
    if (!state?.organisation?.organisationId) {
      navigate(ROUTES.admin.organisations.detail(organisationId), { replace: true });
    }
  }, [state, organisationId, navigate]);

  if (!state?.organisation?.organisationId) return <></>;

  const goBackToReview = () => navigate(ROUTES.admin.organisations.editReview(organisationId), { state });
  const confirmDiscard = () =>
    navigate(ROUTES.admin.organisations.detail(organisationId), {
      replace: true,
      state: {
        banner: {
          title: 'Edits discarded',
          body: `The updates you made to '${name}' were not saved.`,
        },
      },
    });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.organisations.editReview(organisationId), onClick: goBackToReview }}>
      <ConfirmActionPanel testId="admin-local-authorities-edit-discard-panel">
        <H1>Are you sure you want to discard edits?</H1>
        <Paragraph>If you discard your edits, you&apos;ll lose any new information you have added to organisation details for {name}.</Paragraph>
        <div className="govuk-button-group">
          <GovUKButton className="govuk-button govuk-button--inverse" onClick={confirmDiscard} data-testid="admin-local-authorities-edit-discard-confirm">
            Discard edits
          </GovUKButton>
          <RouterLink
            to={ROUTES.admin.organisations.editReview(organisationId)}
            state={state}
            className="govuk-link govuk-link--inverse"
            data-testid="admin-local-authorities-edit-discard-cancel">
            Go back to organisation
          </RouterLink>
        </div>
      </ConfirmActionPanel>
    </GeneralLayout>
  );
};

export default LocalAuthoritiesEditDiscard;
