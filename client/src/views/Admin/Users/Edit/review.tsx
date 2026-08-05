import React, { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useLocation, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from 'react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKButton, GovUKSummaryCard, SummaryList, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { adminUserKey, adminUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { ROLE_LABELS } from '../../../../components/AuthProvider';
import { updateUser } from '../mutations';
import type { EditUserFormState } from './index';
import './styles.css';

const UsersEditReview = (): React.ReactElement => {
  usePageTitle('Check your answers');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();

  const state = location.state as EditUserFormState | null;

  const firstName = state?.firstName ?? '';
  const lastName = state?.lastName ?? '';
  const role = state?.role ?? '';
  const organisationId = state?.organisationId ?? '';
  const organisationName = state?.organisationName ?? '';
  const email = state?.email ?? '';

  const [submitError, setSubmitError] = useState<string | undefined>();
  const [submitAttempts, setSubmitAttempts] = useState(0);

  useErrorSummaryFocus(submitAttempts, !!submitError);

  useEffect(() => {
    if (!state?.userId) {
      navigate(ROUTES.admin.users.edit(userId), { replace: true });
    }
  }, [state, userId, navigate]);

  const { mutate, isLoading } = useMutation(updateUser, {
    onSuccess: () => {
      queryClient.invalidateQueries(adminUsersListKey);
      queryClient.invalidateQueries(adminUserKey(userId));
      navigate(ROUTES.admin.users.detail(userId), {
        replace: true,
        state: {
          banner: {
            title: 'Edits saved',
            body: `The user details for ${firstName} ${lastName} are now up to date.`,
          },
        },
      });
    },
    onError: (error: any) => {
      setSubmitAttempts((n) => n + 1);
      const status = error?.response?.status;
      setSubmitError(status === 409 ? 'A user with this email address already exists.' : 'Could not save the changes. Try again.');
    },
  });

  if (!state?.userId) return <></>;

  const handleSave = () => {
    mutate({ id: userId, firstName, lastName, email, role, organisationId: organisationId || undefined });
  };

  const summaryItems = [
    { label: 'First name', value: firstName },
    { label: 'Last name', value: lastName },
    {
      label: 'Organisation',
      value:
        organisationId && organisationName ? (
          <RouterLink to={ROUTES.admin.organisations.detail(organisationId)} className="govuk-link" data-testid="admin-users-edit-review-organisation-link">
            {organisationName}
          </RouterLink>
        ) : (
          '-'
        ),
    },
    { label: 'Access level', value: ROLE_LABELS[role] ?? role },
    {
      label: 'Email address',
      value: (
        <a href={`mailto:${email}`} className="govuk-link" data-testid="admin-users-edit-review-email-link">
          {email}
        </a>
      ),
    },
  ];

  const errorList = submitError ? [{ targetName: 'submit', text: submitError }] : [];

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.users.edit(userId), onClick: () => navigate(ROUTES.admin.users.edit(userId), { state }) }}>
      <div className="admin-users-edit-review">
        {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

        <div className="admin-users-edit-review__heading">
          <span className="govuk-caption-l">Edit user</span>
          <H1>Check your answers</H1>
        </div>

        <GovUKSummaryCard
          title="User details"
          actions={[
            {
              label: 'Edit',
              onClick: () => navigate(ROUTES.admin.users.edit(userId), { state }),
              visuallyHiddenText: 'user details',
              testId: 'admin-users-edit-review-change',
            },
          ]}
          testId="admin-users-edit-review-card">
          <SummaryList items={summaryItems} equalColumns testId="admin-users-edit-review-details" />
        </GovUKSummaryCard>

        <div className="govuk-button-group admin-users-edit-review__actions">
          <GovUKButton onClick={handleSave} isLoading={isLoading} disabled={isLoading} data-testid="admin-users-edit-review-save">
            Save
          </GovUKButton>
          <GovUKButton
            className="govuk-button govuk-button--secondary"
            onClick={() => navigate(ROUTES.admin.users.editDiscard(userId), { state })}
            disabled={isLoading}
            data-testid="admin-users-edit-review-discard">
            Discard edits
          </GovUKButton>
        </div>
      </div>
    </ManagementLayout>
  );
};

export default UsersEditReview;
