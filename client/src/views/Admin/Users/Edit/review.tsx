import React, { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useLocation, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { httpStatusOf } from '../../../../helpers/httpErrors';
import { ManagementLayout } from '../../../../layouts';
import { GovUKButton, GovUKSummaryCard, SummaryList, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { adminUserKey, adminUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { ROLE_LABELS } from '../../../../components/AuthProvider';
import { getUser } from '../queries';
import { updateUser } from '../mutations';
import { editRefusalMessage } from '../refusals';
import type { EditUserFormState } from './index';
import './styles.css';

// The API returns UserException.Message as a bare JSON string, and those messages interpolate the user and
// organisation GUIDs, so the status code is what we map — never the body. 422 is LastActiveAdminGuard refusing to
// leave DHSC without an active Super Admin, or the user's organisation without an active LA Admin (BLUM-8751): a
// permanent refusal, not something a retry fixes, and reachable from the Organisation hint's own suggestion to
// clear the field to make someone a Super Admin. Which one is told apart by the user's role before the edit.
const editUserErrorMessage = (status: number | undefined, currentRole: string | undefined): string => {
  if (status === 409) return 'A user with this email address already exists.';
  if (status === 422) return editRefusalMessage(currentRole);
  return 'Could not save the changes. Try again.';
};

const UsersEditReview = (): React.ReactElement => {
  usePageTitle('Check your answers');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();

  // The user as stored, which the edit form has already loaded: only its role says which refusal a 422 is.
  const { data: currentUser } = useQuery({ queryKey: adminUserKey(userId), queryFn: () => getUser(userId), enabled: !!userId });

  const state = location.state as EditUserFormState | null;

  const firstName = state?.firstName ?? '';
  const lastName = state?.lastName ?? '';
  const role = state?.role ?? '';
  const organisationId = state?.organisationId ?? '';
  const organisationName = state?.organisationName ?? '';
  const email = state?.email ?? '';

  const [submitError, setSubmitError] = useState<string | undefined>();
  const [submitAttempts, setSubmitAttempts] = useState(0);

  useEffect(() => {
    if (!state?.userId) {
      navigate(ROUTES.admin.users.edit(userId), { replace: true });
    }
  }, [state, userId, navigate]);

  const { mutate, isPending } = useMutation({
    mutationFn: updateUser,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminUsersListKey });
      queryClient.invalidateQueries({ queryKey: adminUserKey(userId) });
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
    onError: (error) => {
      setSubmitAttempts((n) => n + 1);
      setSubmitError(editUserErrorMessage(httpStatusOf(error), currentUser?.data.role));
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
        <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

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
          <GovUKButton onClick={handleSave} isLoading={isPending} disabled={isPending} data-testid="admin-users-edit-review-save">
            Save
          </GovUKButton>
          <GovUKButton
            className="govuk-button govuk-button--secondary"
            onClick={() => navigate(ROUTES.admin.users.editDiscard(userId), { state })}
            disabled={isPending}
            data-testid="admin-users-edit-review-discard">
            Discard edits
          </GovUKButton>
        </div>
      </div>
    </ManagementLayout>
  );
};

export default UsersEditReview;
