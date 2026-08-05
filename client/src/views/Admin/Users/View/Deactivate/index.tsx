import React from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, H1, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { adminUserKey, adminUsersListKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getUser } from '../../queries';
import { deactivateUser } from '../../mutations';
import '../action-flow.css';

const UsersDeactivate = (): React.ReactElement => {
  usePageTitle('Deactivate account');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data } = useQuery(adminUserKey(userId), () => getUser(userId), { enabled: !!userId });
  const user = data?.data;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  const { mutate, isLoading } = useMutation(() => deactivateUser(userId), {
    onSuccess: () => {
      queryClient.invalidateQueries(adminUsersListKey);
      queryClient.invalidateQueries(adminUserKey(userId));
      navigate(ROUTES.admin.users.detail(userId), {
        state: {
          banner: {
            title: 'Account deactivated',
            body: `You can reactivate the account for ${fullName} any time.`,
          },
        },
        replace: true,
      });
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.users.detail(userId) }}>
      <div className="admin-users-confirm-action-page admin-users-deactivate-page">
        <ConfirmActionPanel testId="admin-users-deactivate-panel">
          <H1 data-testid="admin-users-deactivate-heading">Are you sure you want to deactivate the user&rsquo;s account?</H1>
          <Paragraph>Deactivating their account means {fullName} will not be able to sign in to this service.</Paragraph>
          <Paragraph>You can reactivate their account at any time.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isLoading}
              disabled={isLoading}
              data-testid="admin-users-deactivate-confirm">
              Deactivate account
            </GovUKButton>
            <RouterLink to={ROUTES.admin.users.detail(userId)} className="govuk-link govuk-link--inverse" data-testid="admin-users-deactivate-cancel">
              Go back to user
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default UsersDeactivate;
