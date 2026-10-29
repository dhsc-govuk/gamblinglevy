import React, { useEffect, useState } from 'react';
import { useNavigate, useLocation, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { httpMessageOf, httpStatusOf } from '../../../../helpers/httpErrors';
import { OrganisationManagementLayout } from '../../../../layouts';
import { GovUKButton, GovUKSummaryCard, SummaryList, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { organisationUserKey, organisationUsersListKey } from '../../../../helpers/queriesParams';
import { updateOrganisationUser } from '../../../../components/Global/OrganisationUsers/api';
import type { OrgEditFormState } from './index';

interface ReviewErrors {
  submit?: string;
}

const UsersEditReview = (): React.ReactElement => {
  usePageTitle('Check your answers');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const state = location.state as OrgEditFormState | null;

  const firstName = state?.firstName ?? '';
  const lastName = state?.lastName ?? '';
  const email = state?.email ?? '';
  const roleName = state?.roleName ?? '';
  const role = state?.role ?? '';
  const organisationId = state?.organisationId ?? '';
  const status = state?.status ?? 'active';

  const [errors, setErrors] = useState<ReviewErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  useEffect(() => {
    if (!state?.firstName) navigate(`/organisation-admin/users/${userId}/edit`, { replace: true });
  }, [state, navigate, userId]);

  const { mutate, isPending } = useMutation({
    mutationFn: () =>
      updateOrganisationUser(organisationId, {
        id: userId,
        firstName,
        lastName,
        email,
        role,
        status,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: organisationUsersListKey(organisationId) });
      queryClient.invalidateQueries({ queryKey: organisationUserKey(organisationId, userId) });
      navigate(`/organisation-admin/users/${userId}`, {
        state: {
          banner: {
            title: 'Edits saved',
            body: `The user details for ${firstName} ${lastName} are now up to date.`,
          },
        },
      });
    },
    onError: (error) => {
      const httpStatus = httpStatusOf(error);
      setErrors({
        submit: httpStatus === 409 ? 'A user with this email address already exists.' : httpMessageOf(error) ?? 'Could not save the changes. Try again.',
      });
      setSubmitAttempts((n) => n + 1);
    },
  });

  if (!state?.firstName) return <></>;

  const errorList = Object.entries(errors)
    .filter(([, v]) => Boolean(v))
    .map(([key, text]) => ({ targetName: key, text: text ?? '' }));

  return (
    <OrganisationManagementLayout
      backLink={{
        href: `/organisation-admin/users/${userId}/edit`,
        onClick: () => navigate(`/organisation-admin/users/${userId}/edit`, { state }),
      }}>
      <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

      <span className="govuk-caption-l">Edit user</span>
      <H1>Check your answers</H1>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          // No bump here: onError bumps along with the refusal it sets. A bump at this point would be spent on the
          // refusal still showing from the last attempt, pulling focus to it while this request is in flight.
          mutate();
        }}
        noValidate>
        <GovUKSummaryCard
          title="User details"
          actions={[
            {
              label: 'Edit',
              onClick: () => navigate(`/organisation-admin/users/${userId}/edit`, { state }),
              visuallyHiddenText: 'user details',
              testId: 'organisation-admin-users-edit-review-edit',
            },
          ]}
          testId="organisation-admin-users-edit-review-summary">
          <SummaryList
            testId="organisation-admin-users-edit-review-details"
            items={[
              { label: 'First name', value: firstName },
              { label: 'Last name', value: lastName },
              { label: 'Access level', value: roleName },
              {
                label: 'Email address',
                value: (
                  <a href={`mailto:${email}`} className="govuk-link" data-testid="organisation-admin-users-edit-review-email-link">
                    {email}
                  </a>
                ),
              },
            ]}
          />
        </GovUKSummaryCard>

        <div className="govuk-button-group">
          <GovUKButton type="submit" isLoading={isPending} disabled={isPending} data-testid="organisation-admin-users-edit-review-save">
            Save
          </GovUKButton>
          <GovUKButton
            type="button"
            className="govuk-button govuk-button--secondary"
            onClick={() =>
              navigate(`/organisation-admin/users/${userId}/edit/discard`, {
                state: { fullName: `${firstName} ${lastName}`, originalFullName: state?.originalFullName ?? '', organisationId },
              })
            }
            data-testid="organisation-admin-users-edit-review-discard">
            Discard edits
          </GovUKButton>
        </div>
      </form>
    </OrganisationManagementLayout>
  );
};

export default UsersEditReview;
