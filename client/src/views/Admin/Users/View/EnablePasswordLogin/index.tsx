import React from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, H1, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { adminUserKey, adminUsersListKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getUser } from '../../queries';
import { enableUserPasswordLogin } from '../../mutations';
import '../action-flow.css';

const UsersEnablePasswordLogin = (): React.ReactElement => {
  usePageTitle('Enable password login');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data } = useQuery({ queryKey: adminUserKey(userId), queryFn: () => getUser(userId), enabled: !!userId });
  const user = data?.data;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  const { mutate, isPending } = useMutation({
    mutationFn: () => enableUserPasswordLogin(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminUsersListKey });
      queryClient.invalidateQueries({ queryKey: adminUserKey(userId) });
      navigate(ROUTES.admin.users.detail(userId), {
        state: {
          banner: {
            title: 'Password login has been enabled',
            body: `A temporary password has been emailed to ${fullName}. They'll be asked to set a new password the next time they sign in, and any existing session has been signed out.`,
          },
        },
        replace: true,
      });
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.users.detail(userId) }}>
      <div className="admin-users-confirm-action-page admin-users-enable-password-login-page">
        <ConfirmActionPanel testId="admin-users-enable-password-login-panel">
          <H1 data-testid="admin-users-enable-password-login-heading">Are you sure you want to enable password login for {fullName}?</H1>
          <Paragraph>A temporary password will be emailed to them. They&rsquo;ll be asked to set a new password the next time they sign in.</Paragraph>
          <Paragraph>Any existing session will be signed out.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isPending}
              disabled={isPending}
              data-testid="admin-users-enable-password-login-confirm">
              Enable password login
            </GovUKButton>
            <RouterLink to={ROUTES.admin.users.detail(userId)} className="govuk-link govuk-link--inverse" data-testid="admin-users-enable-password-login-cancel">
              Go back to user
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default UsersEnablePasswordLogin;
