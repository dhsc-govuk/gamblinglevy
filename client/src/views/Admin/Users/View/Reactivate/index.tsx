import React from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, H1, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { adminUserKey, adminUsersListKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getUser } from '../../queries';
import { reactivateUser } from '../../mutations';
import '../action-flow.css';

const UsersReactivate = (): React.ReactElement => {
  usePageTitle('Reactivate account');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data } = useQuery({ queryKey: adminUserKey(userId), queryFn: () => getUser(userId), enabled: !!userId });
  const user = data?.data;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  const { mutate, isPending } = useMutation({
    mutationFn: () => reactivateUser(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminUsersListKey });
      queryClient.invalidateQueries({ queryKey: adminUserKey(userId) });
      navigate(ROUTES.admin.users.detail(userId), {
        state: {
          banner: {
            title: 'Account reactivated',
            body: `${fullName} can sign in to this service again.`,
          },
        },
        replace: true,
      });
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.users.detail(userId) }}>
      <div className="admin-users-confirm-action-page admin-users-reactivate-page">
        <ConfirmActionPanel testId="admin-users-reactivate-panel">
          <H1 data-testid="admin-users-reactivate-heading">Are you sure you want to reactivate the user&rsquo;s account?</H1>
          <Paragraph>Reactivating the account for {fullName} means they will be able to sign in to the service again.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isPending}
              disabled={isPending}
              data-testid="admin-users-reactivate-confirm">
              Reactivate account
            </GovUKButton>
            <RouterLink to={ROUTES.admin.users.detail(userId)} className="govuk-link govuk-link--inverse" data-testid="admin-users-reactivate-cancel">
              Go back to user
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default UsersReactivate;
