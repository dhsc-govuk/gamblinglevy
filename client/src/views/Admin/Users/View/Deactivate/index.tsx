import React, { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { httpStatusOf } from '../../../../../helpers/httpErrors';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, GovUKNotificationBanner, H1, Paragraph } from '../../../../../components/GovUKComponents';
import { useAuthProvider } from '../../../../../components/AuthProvider';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { adminUserKey, adminUsersListKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { getUser } from '../../queries';
import { deactivateUser } from '../../mutations';
import { removalRefusalMessage } from '../../refusals';
import '../action-flow.css';

const UsersDeactivate = (): React.ReactElement => {
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  // The profile id is the users.id in the route; the context's userId is the Entra subject, which never matches it.
  const { user: actor } = useAuthProvider();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  // The confirm button is disabled while the request runs, so a refusal moves focus onto the banner explaining it.
  const [errorAttempts, setErrorAttempts] = useState(0);
  usePageTitle(`${errorMessage ? 'Error: ' : ''}Deactivate account`);

  const { data } = useQuery({ queryKey: adminUserKey(userId), queryFn: () => getUser(userId), enabled: !!userId });
  const user = data?.data;
  const fullName = user ? `${user.firstName} ${user.lastName}` : 'this user';

  const { mutate, isPending } = useMutation({
    mutationFn: () => deactivateUser(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminUsersListKey });
      queryClient.invalidateQueries({ queryKey: adminUserKey(userId) });
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
    onError: (error) => {
      const status = httpStatusOf(error);
      setErrorAttempts((n) => n + 1);
      setErrorMessage(
        status === 422
          ? removalRefusalMessage('deactivate', user?.role, !!actor?.id && actor.id === userId)
          : status === 409
          ? 'This account is already deactivated.'
          : 'Could not deactivate the account. Try again.'
      );
    },
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.admin.users.detail(userId) }}>
      {errorMessage && (
        <GovUKNotificationBanner type="important" title="There is a problem" focusTrigger={errorAttempts} testId="admin-users-deactivate-error">
          <p className="govuk-body">{errorMessage}</p>
        </GovUKNotificationBanner>
      )}

      <div className="admin-users-confirm-action-page admin-users-deactivate-page">
        <ConfirmActionPanel testId="admin-users-deactivate-panel">
          <H1 data-testid="admin-users-deactivate-heading">Are you sure you want to deactivate the user&rsquo;s account?</H1>
          <Paragraph>Deactivating their account means {fullName} will not be able to sign in to this service.</Paragraph>
          <Paragraph>You can reactivate their account at any time.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton
              className="govuk-button govuk-button--inverse"
              onClick={() => mutate()}
              isLoading={isPending}
              disabled={isPending}
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
