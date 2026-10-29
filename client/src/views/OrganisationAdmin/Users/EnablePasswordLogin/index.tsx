import React, { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, NotAuthorised, PageLoading, H1, Paragraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { organisationUserKey } from '../../../../helpers/queriesParams';
import { enableOrganisationUserPasswordLogin } from '../../../../components/Global/OrganisationUsers/api';
import { UserLoadFailed, useOrganisationUser, organisationUserPageTitle } from '../../../../components/Global/OrganisationUsers';
import { useAuthProvider } from '../../../../components/AuthProvider';

const UsersEnablePasswordLogin = (): React.ReactElement => {
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { organisationId } = useAuthProvider();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const userQuery = useOrganisationUser(userId);
  const { user, isDenied, loadFailed } = userQuery;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  usePageTitle(organisationUserPageTitle(userQuery, 'Enable password login'));

  const { mutate, isPending } = useMutation({
    mutationFn: () => enableOrganisationUserPasswordLogin(organisationId!, userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: organisationUserKey(organisationId ?? '', userId) });
      navigate(`/organisation-admin/users/${userId}`, {
        state: {
          banner: {
            title: 'Password login has been enabled',
            body: `A temporary password has been emailed to ${fullName}. They'll be asked to set a new password the next time they sign in, and any existing session has been signed out.`,
          },
        },
      });
    },
    onError: () => setErrorMessage('Could not enable password login. Try again.'),
  });

  // Reached by hand-editing the URL to another LA's user id. NotAuthorised renders its own <main>, so it replaces the
  // layout rather than sitting inside it — otherwise the page would carry two main landmarks.
  if (isDenied) return <NotAuthorised goBack={navigate} testId="organisation-admin-users-enable-password-login-not-authorised" />;

  // Gate on the record itself, not on a loading flag — the confirm panel would otherwise paint "this user" before
  // flipping to the real name, the denial or the problem page.
  if (!user) {
    return (
      <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
        {loadFailed ? <UserLoadFailed testId="organisation-admin-users-enable-password-login-load-error" /> : <PageLoading />}
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
      {errorMessage && (
        <GovUKNotificationBanner type="important" title="There is a problem" testId="organisation-admin-users-enable-password-login-error">
          <p className="govuk-body">{errorMessage}</p>
        </GovUKNotificationBanner>
      )}

      <ConfirmActionPanel testId="organisation-admin-users-enable-password-login-panel">
        <H1 data-testid="organisation-admin-users-enable-password-login-heading">Are you sure you want to enable password login for {fullName}?</H1>
        <Paragraph>A temporary password will be emailed to them. They&apos;ll be asked to set a new password the next time they sign in.</Paragraph>
        <Paragraph>Any existing session will be signed out.</Paragraph>
        <div className="govuk-button-group">
          <GovUKButton
            className="govuk-button govuk-button--inverse"
            onClick={() => mutate()}
            isLoading={isPending}
            disabled={isPending}
            data-testid="organisation-admin-users-enable-password-login-confirm">
            Enable password login
          </GovUKButton>
          <RouterLink to={`/organisation-admin/users/${userId}`} className="govuk-link govuk-link--inverse" data-testid="organisation-admin-users-enable-password-login-cancel">
            Go back to user
          </RouterLink>
        </div>
      </ConfirmActionPanel>
    </GeneralLayout>
  );
};

export default UsersEnablePasswordLogin;
