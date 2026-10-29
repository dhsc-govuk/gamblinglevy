import React, { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { httpMessageOf } from '../../../../../helpers/httpErrors';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { localAuthoritiesListKey, localAuthorityKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getLocalAuthority } from '../../queries';
import { deleteLocalAuthority } from '../../mutations';
import './styles.css';

const LocalAuthoritiesDelete = (): React.ReactElement => {
  usePageTitle('Delete organisation account');
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const { data } = useQuery({ queryKey: localAuthorityKey(organisationId), queryFn: () => getLocalAuthority(organisationId), enabled: !!organisationId });
  const name = data?.data?.name ?? 'this organisation';

  const { mutate, isPending } = useMutation({
    mutationFn: () => deleteLocalAuthority(organisationId, true),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: localAuthoritiesListKey() });
      queryClient.removeQueries({ queryKey: localAuthorityKey(organisationId) });
      navigate(ROUTES.admin.organisations.list, {
        state: {
          banner: {
            title: 'Organisation deleted',
            body: `Users from '${name}' will not be able to sign in to the service.`,
          },
        },
        replace: true,
      });
    },
    onError: (error) => {
      setErrorMessage(httpMessageOf(error) ?? 'Could not delete the organisation account. Try again.');
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.organisations.detail(organisationId) }}>
      {errorMessage && (
        <GovUKNotificationBanner type="important" title="There is a problem" testId="admin-local-authorities-delete-error">
          <p className="govuk-body">{errorMessage}</p>
        </GovUKNotificationBanner>
      )}

      <div className="admin-local-authorities-delete-page">
        <ConfirmActionPanel testId="admin-local-authorities-delete-panel">
          <h1 className="govuk-heading-l" data-testid="admin-local-authorities-delete-heading">
            Are you sure you want to delete the organisation&rsquo;s account?
          </h1>
          <Paragraph>Deleting the account for &lsquo;{name}&rsquo; will permanently delete its users&apos; details and stop them from signing in to this service.</Paragraph>
          <Paragraph>Any reports already submitted for the organisation will not be deleted.</Paragraph>
          <Paragraph>This action cannot be undone.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isPending}
              disabled={isPending}
              data-testid="admin-local-authorities-delete-confirm">
              Delete account
            </GovUKButton>
            <RouterLink to={ROUTES.admin.organisations.detail(organisationId)} className="govuk-link govuk-link--inverse" data-testid="admin-local-authorities-delete-cancel">
              Go back to organisation
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default LocalAuthoritiesDelete;
