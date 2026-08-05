import React from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { localAuthoritiesListKey, localAuthorityKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getLocalAuthority } from '../../queries';
import { suspendLocalAuthority } from '../../mutations';
import './styles.css';

const LocalAuthoritiesSuspend = (): React.ReactElement => {
  usePageTitle('Deactivate organisation account');
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data } = useQuery(localAuthorityKey(organisationId), () => getLocalAuthority(organisationId), { enabled: !!organisationId });
  const name = data?.data?.name ?? 'this organisation';

  const { mutate, isLoading } = useMutation(() => suspendLocalAuthority(organisationId), {
    onSuccess: () => {
      queryClient.invalidateQueries(localAuthoritiesListKey());
      queryClient.invalidateQueries(localAuthorityKey(organisationId));
      navigate(ROUTES.admin.organisations.detail(organisationId), {
        state: {
          banner: {
            title: 'Account deactivated',
            body: `Users from '${name}' will not be able to sign in to this service. You can reactivate the account any time.`,
          },
        },
        replace: true,
      });
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.organisations.detail(organisationId) }}>
      <div className="admin-local-authorities-suspend-page">
        <ConfirmActionPanel testId="admin-local-authorities-suspend-panel">
          <h1 className="govuk-heading-l" data-testid="admin-local-authorities-suspend-heading">
            Are you sure you want to deactivate the organisation&rsquo;s account?
          </h1>
          <Paragraph>
            Deactivating the account for &lsquo;{name}&rsquo; means its users will not be able to sign in to this service.
          </Paragraph>
          <Paragraph>You can reactivate the account at any time.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isLoading}
              disabled={isLoading}
              data-testid="admin-local-authorities-suspend-confirm">
              Deactivate account
            </GovUKButton>
            <RouterLink to={ROUTES.admin.organisations.detail(organisationId)} className="govuk-link govuk-link--inverse" data-testid="admin-local-authorities-suspend-cancel">
              Go back to organisation
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default LocalAuthoritiesSuspend;
