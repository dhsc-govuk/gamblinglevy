import React from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, H1, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { adminUserKey, adminUsersListKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getUser } from '../../queries';
import { resetUserPassword } from '../../mutations';
import '../action-flow.css';

const UsersResetPassword = (): React.ReactElement => {
  usePageTitle('Reset password');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data } = useQuery({ queryKey: adminUserKey(userId), queryFn: () => getUser(userId), enabled: !!userId });
  const user = data?.data;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  const { mutate, isPending } = useMutation({
    mutationFn: () => resetUserPassword(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminUsersListKey });
      queryClient.invalidateQueries({ queryKey: adminUserKey(userId) });
      navigate(ROUTES.admin.users.detail(userId), {
        state: {
          banner: {
            title: "This user's password has been reset",
            body: `A new temporary password has been emailed to ${fullName}. Their current password no longer works, and any existing session has been signed out.`,
          },
        },
        replace: true,
      });
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.users.detail(userId) }}>
      <div className="admin-users-confirm-action-page admin-users-reset-password-page">
        <ConfirmActionPanel testId="admin-users-reset-password-panel">
          <H1 data-testid="admin-users-reset-password-heading">Are you sure you want to reset {fullName}&rsquo;s password?</H1>
          <Paragraph>A new temporary password will be emailed to them, and their current password will stop working immediately.</Paragraph>
          <Paragraph>Any existing session will be signed out.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isPending}
              disabled={isPending}
              data-testid="admin-users-reset-password-confirm">
              Reset password
            </GovUKButton>
            <RouterLink to={ROUTES.admin.users.detail(userId)} className="govuk-link govuk-link--inverse" data-testid="admin-users-reset-password-cancel">
              Go back to user
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default UsersResetPassword;
