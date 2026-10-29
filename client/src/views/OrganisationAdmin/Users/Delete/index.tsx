import React, { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, NotAuthorised, PageLoading, H1, Paragraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { organisationUserKey, organisationUsersListKey } from '../../../../helpers/queriesParams';
import { deleteOrganisationUser } from '../../../../components/Global/OrganisationUsers/api';
import { UserLoadFailed, useOrganisationUser, organisationUserPageTitle } from '../../../../components/Global/OrganisationUsers';
import { useAuthProvider } from '../../../../components/AuthProvider';

const UsersDelete = (): React.ReactElement => {
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { organisationId } = useAuthProvider();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const userQuery = useOrganisationUser(userId);
  const { user, isDenied, loadFailed } = userQuery;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  usePageTitle(organisationUserPageTitle(userQuery, 'Delete user account'));

  const { mutate, isPending } = useMutation({
    mutationFn: () => deleteOrganisationUser(organisationId!, userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: organisationUsersListKey(organisationId ?? '') });
      queryClient.removeQueries({ queryKey: organisationUserKey(organisationId ?? '', userId) });
      navigate('/organisation-admin/users', {
        state: {
          banner: {
            title: 'Account deleted',
            body: `${fullName} will not be able to sign in to the service.`,
          },
        },
      });
    },
    onError: () => setErrorMessage('Could not delete the account. Try again.'),
  });

  // Reached by hand-editing the URL to another LA's user id. NotAuthorised renders its own <main>, so it replaces the
  // layout rather than sitting inside it — otherwise the page would carry two main landmarks.
  if (isDenied) return <NotAuthorised goBack={navigate} testId="organisation-admin-users-delete-not-authorised" />;

  // Gate on the record itself, not on a loading flag — the confirm panel would otherwise paint "this user" before
  // flipping to the real name, the denial or the problem page.
  if (!user) {
    return (
      <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
        {loadFailed ? <UserLoadFailed testId="organisation-admin-users-delete-load-error" /> : <PageLoading />}
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
      {errorMessage && (
        <GovUKNotificationBanner type="important" title="There is a problem" testId="organisation-admin-users-delete-error">
          <p className="govuk-body">{errorMessage}</p>
        </GovUKNotificationBanner>
      )}

      <ConfirmActionPanel testId="organisation-admin-users-delete-panel">
        <H1 data-testid="organisation-admin-users-delete-heading">Are you sure you want to delete the user&apos;s account?</H1>
        <Paragraph>Deleting the account for {fullName} will permanently remove their access to the service and delete their details.</Paragraph>
        <Paragraph>It will not remove saved or draft information they have already created in the service.</Paragraph>
        <Paragraph>This action cannot be undone.</Paragraph>
        <div className="govuk-button-group">
          <GovUKButton
            className="govuk-button govuk-button--inverse"
            onClick={() => mutate()}
            isLoading={isPending}
            disabled={isPending}
            data-testid="organisation-admin-users-delete-confirm">
            Delete account
          </GovUKButton>
          <RouterLink to={`/organisation-admin/users/${userId}`} className="govuk-link govuk-link--inverse" data-testid="organisation-admin-users-delete-cancel">
            Go back to user
          </RouterLink>
        </div>
      </ConfirmActionPanel>
    </GeneralLayout>
  );
};

export default UsersDelete;
