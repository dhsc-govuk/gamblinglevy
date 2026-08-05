import React, { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, NotAuthorised, PageLoading, H1, Paragraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { organisationUserKey } from '../../../../helpers/queriesParams';
import { resetOrganisationUserMfa } from '../../../../components/Global/OrganisationUsers/api';
import { UserLoadFailed, useOrganisationUser, organisationUserPageTitle } from '../../../../components/Global/OrganisationUsers';
import { useAuthProvider } from '../../../../components/AuthProvider';

const UsersResetMfa = (): React.ReactElement => {
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { organisationId } = useAuthProvider();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const userQuery = useOrganisationUser(userId);
  const { user, isDenied, loadFailed } = userQuery;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  usePageTitle(organisationUserPageTitle(userQuery, 'Reset MFA'));

  const { mutate, isLoading } = useMutation(() => resetOrganisationUserMfa(organisationId!, userId), {
    onSuccess: () => {
      queryClient.invalidateQueries(organisationUserKey(organisationId ?? '', userId));
      navigate(`/organisation-admin/users/${userId}`, {
        state: {
          banner: {
            title: 'Multi-factor authentication has been reset',
            body: `${fullName} will need to set up their authenticator app the next time they sign in.`,
          },
        },
      });
    },
    onError: () => setErrorMessage('Could not reset MFA. Try again.'),
  });

  // Reached by hand-editing the URL to another LA's user id. NotAuthorised renders its own <main>, so it replaces the
  // layout rather than sitting inside it — otherwise the page would carry two main landmarks.
  if (isDenied) return <NotAuthorised goBack={navigate} testId="organisation-admin-users-reset-mfa-not-authorised" />;

  // Gate on the record itself, not on a loading flag — the confirm panel would otherwise paint "this user" before
  // flipping to the real name, the denial or the problem page.
  if (!user) {
    return (
      <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
        {loadFailed ? <UserLoadFailed testId="organisation-admin-users-reset-mfa-load-error" /> : <PageLoading />}
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
      {errorMessage && (
        <GovUKNotificationBanner type="important" title="There is a problem" testId="organisation-admin-users-reset-mfa-error">
          <p className="govuk-body">{errorMessage}</p>
        </GovUKNotificationBanner>
      )}

      <ConfirmActionPanel testId="organisation-admin-users-reset-mfa-panel">
        <H1 data-testid="organisation-admin-users-reset-mfa-heading">Are you sure you want to reset multi-factor authentication (MFA)?</H1>
        <Paragraph>Resetting MFA for {fullName} means they will not be able to sign in to this service.</Paragraph>
        <Paragraph>They&apos;ll need to set up their authenticator app again the next time they sign in.</Paragraph>
        <div className="govuk-button-group">
          <GovUKButton
            className="govuk-button govuk-button--inverse"
            onClick={() => mutate()}
            isLoading={isLoading}
            disabled={isLoading}
            data-testid="organisation-admin-users-reset-mfa-confirm">
            Reset MFA
          </GovUKButton>
          <RouterLink to={`/organisation-admin/users/${userId}`} className="govuk-link govuk-link--inverse" data-testid="organisation-admin-users-reset-mfa-cancel">
            Go back to user
          </RouterLink>
        </div>
      </ConfirmActionPanel>
    </GeneralLayout>
  );
};

export default UsersResetMfa;
