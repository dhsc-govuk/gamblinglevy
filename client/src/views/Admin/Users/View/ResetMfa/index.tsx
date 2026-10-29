import React from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, H1, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { adminUserKey, adminUsersListKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getUser } from '../../queries';
import { resetUserMfa } from '../../mutations';
import '../action-flow.css';

const UsersResetMfa = (): React.ReactElement => {
  usePageTitle('Reset MFA');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data } = useQuery({ queryKey: adminUserKey(userId), queryFn: () => getUser(userId), enabled: !!userId });
  const user = data?.data;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  const { mutate, isPending } = useMutation({
    mutationFn: () => resetUserMfa(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminUsersListKey });
      queryClient.invalidateQueries({ queryKey: adminUserKey(userId) });
      navigate(ROUTES.admin.users.detail(userId), {
        state: {
          banner: {
            title: 'Multi-factor authentication has been reset',
            body: `${fullName} will need to set up their authenticator app the next time they sign in.`,
          },
        },
        replace: true,
      });
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.users.detail(userId) }}>
      <div className="admin-users-confirm-action-page admin-users-reset-mfa-page">
        <ConfirmActionPanel testId="admin-users-reset-mfa-panel">
          <H1 data-testid="admin-users-reset-mfa-heading">Are you sure you want to reset multi-factor authentication (MFA)?</H1>
          <Paragraph>Resetting MFA for {fullName} means they will not be able to sign in to this service.</Paragraph>
          <Paragraph>They&rsquo;ll need to set up their authenticator app again the next time they sign in.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isPending}
              disabled={isPending}
              data-testid="admin-users-reset-mfa-confirm">
              Reset MFA
            </GovUKButton>
            <RouterLink to={ROUTES.admin.users.detail(userId)} className="govuk-link govuk-link--inverse" data-testid="admin-users-reset-mfa-cancel">
              Go back to user
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default UsersResetMfa;
