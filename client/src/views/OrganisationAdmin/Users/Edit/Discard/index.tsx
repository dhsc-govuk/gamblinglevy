import React from 'react';
import { Link as RouterLink, useNavigate, useLocation, useParams } from 'react-router-dom';
import { GeneralLayout } from '../../../../../layouts';
import { ConfirmActionPanel, GovUKButton, H1, Paragraph } from '../../../../../components/GovUKComponents';
import usePageTitle from '../../../../../hooks/usePageTitle';

interface DiscardState {
  fullName?: string;
  originalFullName?: string;
}

const UsersEditDiscard = (): React.ReactElement => {
  usePageTitle('Discard edits');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const state = (location.state as DiscardState | null) ?? {};
  const fullName = state.fullName ?? state.originalFullName ?? 'this user';

  const goBackToReview = () => navigate(`/organisation-admin/users/${userId}/edit/review`, { state });
  const confirmDiscard = () =>
    navigate(`/organisation-admin/users/${userId}`, {
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
        href: `/organisation-admin/users/${userId}/edit/review`,
        onClick: goBackToReview,
      }}>
      <ConfirmActionPanel testId="organisation-admin-users-edit-discard-panel">
        <H1>Are you sure you want to discard edits?</H1>
        <Paragraph>If you discard your edits, you&apos;ll lose any new information you have added to user details for {fullName}.</Paragraph>
        <div className="govuk-button-group">
          <GovUKButton className="govuk-button govuk-button--inverse" onClick={confirmDiscard} data-testid="organisation-admin-users-edit-discard-confirm">
            Discard edits
          </GovUKButton>
          <RouterLink
            to={`/organisation-admin/users/${userId}/edit/review`}
            state={state}
            className="govuk-link govuk-link--inverse"
            data-testid="organisation-admin-users-edit-discard-cancel">
            Go back to user
          </RouterLink>
        </div>
      </ConfirmActionPanel>
    </GeneralLayout>
  );
};

export default UsersEditDiscard;
