import React from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { localAuthoritiesListKey, localAuthorityKey, organisationUsersListKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getLocalAuthority } from '../../queries';
import { suspendLocalAuthority } from '../../mutations';
import './styles.css';

const LocalAuthoritiesSuspend = (): React.ReactElement => {
  usePageTitle('Deactivate organisation account');
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data } = useQuery({ queryKey: localAuthorityKey(organisationId), queryFn: () => getLocalAuthority(organisationId), enabled: !!organisationId });
  const name = data?.data?.name ?? 'this organisation';

  // A failure part-way can leave some users deactivated while the account is still active. Trying again
  // finishes the job, so the admin needs to see that it did not complete — isError clears on the next attempt.
  const { mutate, isPending, isError } = useMutation({
    mutationFn: () => suspendLocalAuthority(organisationId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: localAuthoritiesListKey() });
      queryClient.invalidateQueries({ queryKey: localAuthorityKey(organisationId) });
      // Deactivating the account deactivates its users too (BLUM-7667), so the organisation's user list has changed.
      queryClient.invalidateQueries({ queryKey: organisationUsersListKey(organisationId) });
      navigate(ROUTES.admin.organisations.detail(organisationId), {
        state: {
          banner: {
            title: 'Account deactivated',
            body: `The account for '${name}' and all of its users have been deactivated.`,
          },
        },
        replace: true,
      });
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.organisations.detail(organisationId) }}>
      {isError && (
        <GovUKNotificationBanner type="important" title="There is a problem" testId="admin-local-authorities-suspend-error">
          <p className="govuk-body">Could not deactivate the organisation account. Try again.</p>
        </GovUKNotificationBanner>
      )}

      <div className="admin-local-authorities-suspend-page">
        <ConfirmActionPanel testId="admin-local-authorities-suspend-panel">
          <h1 className="govuk-heading-l" data-testid="admin-local-authorities-suspend-heading">
            Are you sure you want to deactivate the organisation&rsquo;s account?
          </h1>
          <Paragraph>
            Deactivating the account for &lsquo;{name}&rsquo; will also deactivate all of its users. They will be signed out and will not be able to sign in to this service.
          </Paragraph>
          <Paragraph>If you reactivate the account later, its users will stay deactivated. You will need to reactivate each user separately.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isPending}
              disabled={isPending}
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
