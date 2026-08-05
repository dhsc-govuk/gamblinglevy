import React, { useEffect } from 'react';
import { Link as RouterLink, useNavigate, useLocation, useParams } from 'react-router-dom';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, H1, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';
import { ROUTES } from '../../../../../helpers/routes';
import type { EditUserFormState } from '../index';
import '../../View/action-flow.css';

const UsersEditDiscard = (): React.ReactElement => {
  usePageTitle('Discard edits');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const location = useLocation();

  const state = location.state as EditUserFormState | null;
  const fullName = state?.originalFullName || `${state?.firstName ?? ''} ${state?.lastName ?? ''}`.trim() || 'this user';

  // No edits to discard if the user landed here without form state — bounce to the user view.
  useEffect(() => {
    if (!state?.userId) {
      navigate(ROUTES.admin.users.detail(userId), { replace: true });
    }
  }, [state, userId, navigate]);

  if (!state?.userId) return <></>;

  const goBackToReview = () => navigate(ROUTES.admin.users.editReview(userId), { state });
  const confirmDiscard = () =>
    navigate(ROUTES.admin.users.detail(userId), {
      replace: true,
      state: {
        banner: {
          title: 'Edits discarded',
          body: `The updates you made to the user details for ${fullName} were not saved.`,
        },
      },
    });

  return (
    <GeneralLayout
      backLink={{
        href: ROUTES.admin.users.editReview(userId),
        onClick: goBackToReview,
      }}>
      <div className="admin-users-confirm-action-page admin-users-edit-discard-page">
        <ConfirmActionPanel testId="admin-users-edit-discard-panel">
          <H1>Are you sure you want to discard edits?</H1>
          <Paragraph>If you discard your edits, you&apos;ll lose any new information you have added to user details for {fullName}.</Paragraph>
          <div className="govuk-button-group">
            <GovUKButton className="govuk-button govuk-button--inverse" onClick={confirmDiscard} data-testid="admin-users-edit-discard-confirm">
              Discard edits
            </GovUKButton>
            <RouterLink to={ROUTES.admin.users.editReview(userId)} state={state} className="govuk-link govuk-link--inverse" data-testid="admin-users-edit-discard-cancel">
              Go back to user
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default UsersEditDiscard;
