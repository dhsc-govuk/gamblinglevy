import React from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, Paragraph } from '../../../../../components/GovUKComponents';
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

  const { data } = useQuery({ queryKey: localAuthorityKey(organisationId), queryFn: () => getLocalAuthority(organisationId), enabled: !!organisationId });
  const name = data?.data?.name ?? 'this organisation';

  const { mutate, isPending, isError } = useMutation({
    mutationFn: () => reinstateLocalAuthority(organisationId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: localAuthoritiesListKey() });
      queryClient.invalidateQueries({ queryKey: localAuthorityKey(organisationId) });
      // Users deactivated along with the account stay deactivated (BLUM-7667), so this must not promise they can sign in.
      navigate(ROUTES.admin.organisations.detail(organisationId), {
        state: {
          banner: {
            title: 'Account reactivated',
            body: `Deactivated users from '${name}' need to be reactivated individually before they can sign in.`,
          },
        },
        replace: true,
      });
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.organisations.detail(organisationId) }}>
      {isError && (
        <GovUKNotificationBanner type="important" title="There is a problem" testId="admin-local-authorities-reinstate-error">
          <p className="govuk-body">Could not reactivate the organisation account. Try again.</p>
        </GovUKNotificationBanner>
      )}

      <div className="admin-local-authorities-reinstate-page">
        <ConfirmActionPanel testId="admin-local-authorities-reinstate-panel">
          <h1 className="govuk-heading-l" data-testid="admin-local-authorities-reinstate-heading">
            Are you sure you want to reactivate the organisation&rsquo;s account?
          </h1>
          <Paragraph>
            Active users from &lsquo;{name}&rsquo; will be able to sign in again. Deactivated users will stay deactivated. You will need to reactivate each of them separately.
          </Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isPending}
              disabled={isPending}
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
