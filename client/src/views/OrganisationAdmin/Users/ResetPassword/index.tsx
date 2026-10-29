import React, { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, NotAuthorised, PageLoading, H1, Paragraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { organisationUserKey } from '../../../../helpers/queriesParams';
import { resetOrganisationUserPassword } from '../../../../components/Global/OrganisationUsers/api';
import { UserLoadFailed, useOrganisationUser, organisationUserPageTitle } from '../../../../components/Global/OrganisationUsers';
import { useAuthProvider } from '../../../../components/AuthProvider';

const UsersResetPassword = (): React.ReactElement => {
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { organisationId } = useAuthProvider();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const userQuery = useOrganisationUser(userId);
  const { user, isDenied, loadFailed } = userQuery;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  usePageTitle(organisationUserPageTitle(userQuery, 'Reset password'));

  const { mutate, isPending } = useMutation({
    mutationFn: () => resetOrganisationUserPassword(organisationId!, userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: organisationUserKey(organisationId ?? '', userId) });
      navigate(`/organisation-admin/users/${userId}`, {
        state: {
          banner: {
            title: "This user's password has been reset",
            body: `A new temporary password has been emailed to ${fullName}. Their current password no longer works, and any existing session has been signed out.`,
          },
        },
      });
    },
    onError: () => setErrorMessage('Could not reset the password. Try again.'),
  });

  // Reached by hand-editing the URL to another LA's user id. NotAuthorised renders its own <main>, so it replaces the
  // layout rather than sitting inside it — otherwise the page would carry two main landmarks.
  if (isDenied) return <NotAuthorised goBack={navigate} testId="organisation-admin-users-reset-password-not-authorised" />;

  // Gate on the record itself, not on a loading flag — the confirm panel would otherwise paint "this user" before
  // flipping to the real name, the denial or the problem page.
  if (!user) {
    return (
      <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
        {loadFailed ? <UserLoadFailed testId="organisation-admin-users-reset-password-load-error" /> : <PageLoading />}
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
      {errorMessage && (
        <GovUKNotificationBanner type="important" title="There is a problem" testId="organisation-admin-users-reset-password-error">
          <p className="govuk-body">{errorMessage}</p>
        </GovUKNotificationBanner>
      )}

      <ConfirmActionPanel testId="organisation-admin-users-reset-password-panel">
        <H1 data-testid="organisation-admin-users-reset-password-heading">Are you sure you want to reset {fullName}&apos;s password?</H1>
        <Paragraph>A new temporary password will be emailed to them, and their current password will stop working immediately.</Paragraph>
        <Paragraph>Any existing session will be signed out.</Paragraph>
        <div className="govuk-button-group">
          <GovUKButton
            className="govuk-button govuk-button--inverse"
            onClick={() => mutate()}
            isLoading={isPending}
            disabled={isPending}
            data-testid="organisation-admin-users-reset-password-confirm">
            Reset password
          </GovUKButton>
          <RouterLink to={`/organisation-admin/users/${userId}`} className="govuk-link govuk-link--inverse" data-testid="organisation-admin-users-reset-password-cancel">
            Go back to user
          </RouterLink>
        </div>
      </ConfirmActionPanel>
    </GeneralLayout>
  );
};

export default UsersResetPassword;
