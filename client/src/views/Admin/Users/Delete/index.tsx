import React, { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, H1, Paragraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { adminUserKey, adminUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getUser } from '../queries';
import { deleteUser } from '../mutations';
import '../View/action-flow.css';

const UsersDelete = (): React.ReactElement => {
  usePageTitle('Delete user account');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const { data } = useQuery(adminUserKey(userId), () => getUser(userId), { enabled: !!userId });
  const user = data?.data;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  const { mutate, isLoading } = useMutation(() => deleteUser(userId), {
    onSuccess: () => {
      queryClient.invalidateQueries(adminUsersListKey);
      queryClient.removeQueries(adminUserKey(userId));
      navigate(ROUTES.admin.users.list, {
        state: {
          banner: {
            title: 'Account deleted',
            body: `${fullName} will not be able to sign in to the service.`,
          },
        },
        replace: true,
      });
    },
    onError: (error: any) => {
      const message = error?.response?.data?.toString() ?? 'Could not delete the account. Try again.';
      setErrorMessage(message);
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.users.detail(userId) }}>
      {errorMessage && (
        <GovUKNotificationBanner type="important" title="There is a problem" testId="admin-users-delete-error">
          <p className="govuk-body">{errorMessage}</p>
        </GovUKNotificationBanner>
      )}

      <div className="admin-users-confirm-action-page admin-users-delete-page">
        <ConfirmActionPanel testId="admin-users-delete-panel">
          <H1 data-testid="admin-users-delete-heading">Are you sure you want to delete the user&rsquo;s account?</H1>
          <Paragraph>Deleting the account for {fullName} will permanently remove their access to the service and delete their details.</Paragraph>
          <Paragraph>It will not remove saved or draft information they have already created in the service.</Paragraph>
          <Paragraph>This action cannot be undone.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton className="govuk-button govuk-button--inverse" onClick={() => mutate()} isLoading={isLoading} disabled={isLoading} data-testid="admin-users-delete-confirm">
              Delete account
            </GovUKButton>
            <RouterLink to={ROUTES.admin.users.detail(userId)} className="govuk-link govuk-link--inverse" data-testid="admin-users-delete-cancel">
              Go back to user
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default UsersDelete;
