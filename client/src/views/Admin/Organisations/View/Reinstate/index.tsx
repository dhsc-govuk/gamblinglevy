import React from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { localAuthoritiesListKey, localAuthorityKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getLocalAuthority } from '../../queries';
import { reinstateLocalAuthority } from '../../mutations';
import './styles.css';

const LocalAuthoritiesReinstate = (): React.ReactElement => {
  usePageTitle('Reactivate organisation account');
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data } = useQuery(localAuthorityKey(organisationId), () => getLocalAuthority(organisationId), { enabled: !!organisationId });
  const name = data?.data?.name ?? 'this organisation';

  const { mutate, isLoading } = useMutation(() => reinstateLocalAuthority(organisationId), {
    onSuccess: () => {
      queryClient.invalidateQueries(localAuthoritiesListKey());
      queryClient.invalidateQueries(localAuthorityKey(organisationId));
      navigate(ROUTES.admin.organisations.detail(organisationId), {
        state: {
          banner: {
            title: 'Account reactivated',
            body: `Users from '${name}' can sign in to this service again.`,
          },
        },
        replace: true,
      });
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.organisations.detail(organisationId) }}>
      <div className="admin-local-authorities-reinstate-page">
        <ConfirmActionPanel testId="admin-local-authorities-reinstate-panel">
          <h1 className="govuk-heading-l" data-testid="admin-local-authorities-reinstate-heading">
            Are you sure you want to reactivate the organisation&rsquo;s account?
          </h1>
          <Paragraph>Reactivating the account means users from &lsquo;{name}&rsquo; will be able to sign in to the service again.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isLoading}
              disabled={isLoading}
              data-testid="admin-local-authorities-reinstate-confirm">
              Reactivate account
            </GovUKButton>
            <RouterLink to={ROUTES.admin.organisations.detail(organisationId)} className="govuk-link govuk-link--inverse" data-testid="admin-local-authorities-reinstate-cancel">
              Go back to organisation
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default LocalAuthoritiesReinstate;
