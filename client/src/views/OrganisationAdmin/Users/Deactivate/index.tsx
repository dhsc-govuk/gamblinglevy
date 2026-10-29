import React, { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, NotAuthorised, PageLoading, H1, Paragraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { organisationUserKey, organisationUsersListKey } from '../../../../helpers/queriesParams';
import { deactivateOrganisationUser, reactivateOrganisationUser } from '../../../../components/Global/OrganisationUsers/api';
import { UserLoadFailed, useOrganisationUser, organisationUserPageTitle } from '../../../../components/Global/OrganisationUsers';
import { useAuthProvider } from '../../../../components/AuthProvider';

const UsersDeactivate = (): React.ReactElement => {
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { organisationId } = useAuthProvider();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const userQuery = useOrganisationUser(userId);
  const { user, isDenied, loadFailed } = userQuery;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';
  const isActive = user?.status === 'active';
  const actionPageTitle = user && !isActive ? 'Reactivate account' : 'Deactivate account';

  usePageTitle(organisationUserPageTitle(userQuery, actionPageTitle));

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: organisationUsersListKey(organisationId ?? '') });
    queryClient.invalidateQueries({ queryKey: organisationUserKey(organisationId ?? '', userId) });
  };

  const { mutate: deactivate, isPending: deactivating } = useMutation({
    mutationFn: () => deactivateOrganisationUser(organisationId!, userId),
    onSuccess: () => {
      invalidate();
      navigate(`/organisation-admin/users/${userId}`, {
        state: {
          banner: {
            title: 'Account deactivated',
            body: `You can reactivate the account for ${fullName} any time.`,
          },
        },
      });
    },
    onError: () => setErrorMessage('Could not deactivate the account. Try again.'),
  });

  const { mutate: reactivate, isPending: reactivating } = useMutation({
    mutationFn: () => reactivateOrganisationUser(organisationId!, userId),
    onSuccess: () => {
      invalidate();
      navigate(`/organisation-admin/users/${userId}`, {
        state: {
          banner: {
            title: 'Account reactivated',
            body: `${fullName} can sign in to this service again.`,
          },
        },
      });
    },
    onError: () => setErrorMessage('Could not reactivate the account. Try again.'),
  });

  const isLoading = deactivating || reactivating;

  // Reached by hand-editing the URL to another LA's user id. NotAuthorised renders its own <main>, so it replaces the
  // layout rather than sitting inside it — otherwise the page would carry two main landmarks.
  if (isDenied) return <NotAuthorised goBack={navigate} testId="organisation-admin-users-deactivate-not-authorised" />;

  // Gate on the record itself, not on a loading flag — the confirm panel would otherwise paint "this user" before
  // flipping to the real name, the denial or the problem page.
  if (!user) {
    return (
      <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
        {loadFailed ? <UserLoadFailed testId="organisation-admin-users-deactivate-load-error" /> : <PageLoading />}
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
      {errorMessage && (
        <GovUKNotificationBanner type="important" title="There is a problem" testId="organisation-admin-users-deactivate-error">
          <p className="govuk-body">{errorMessage}</p>
        </GovUKNotificationBanner>
      )}

      {isActive ? (
        <ConfirmActionPanel testId="organisation-admin-users-deactivate-panel">
          <H1 data-testid="organisation-admin-users-deactivate-heading">Are you sure you want to deactivate the user&apos;s account?</H1>
          <Paragraph>Deactivating their account means {fullName} will not be able to sign in to this service.</Paragraph>
          <Paragraph>You can reactivate their account at any time.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => deactivate()}
              isLoading={isLoading}
              disabled={isLoading}
              data-testid="organisation-admin-users-deactivate-confirm">
              Deactivate account
            </GovUKButton>
            <RouterLink to={`/organisation-admin/users/${userId}`} className="govuk-link govuk-link--inverse" data-testid="organisation-admin-users-deactivate-cancel">
              Go back to user
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      ) : (
        <ConfirmActionPanel testId="organisation-admin-users-reactivate-panel">
          <H1 data-testid="organisation-admin-users-reactivate-heading">Are you sure you want to reactivate the user&apos;s account?</H1>
          <Paragraph>Reactivating the account for {fullName} means they will be able to sign in to the service again.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => reactivate()}
              isLoading={isLoading}
              disabled={isLoading}
              data-testid="organisation-admin-users-reactivate-confirm">
              Reactivate account
            </GovUKButton>
            <RouterLink to={`/organisation-admin/users/${userId}`} className="govuk-link govuk-link--inverse" data-testid="organisation-admin-users-reactivate-cancel">
              Go back to user
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      )}
    </GeneralLayout>
  );
};

export default UsersDeactivate;
