import React, { useEffect, useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useMutation, useQueryClient } from 'react-query';
import { OrganisationManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, GovUKSummaryCard, SummaryList, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { organisationUsersListKey } from '../../../../helpers/queriesParams';
import { createOrganisationUser } from '../../../../components/Global/OrganisationUsers/api';
import type { OrgCreateFormState } from './index';

interface ReviewErrors {
  isActive?: string;
  submit?: string;
}

const UsersCreateReview = (): React.ReactElement => {
  usePageTitle('Check your answers');
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const state = location.state as OrgCreateFormState | null;

  const firstName = state?.firstName ?? '';
  const lastName = state?.lastName ?? '';
  const email = state?.email ?? '';
  const role = state?.role ?? '';
  const roleName = state?.roleName ?? '';
  const organisationId = state?.organisationId ?? '';

  const [isActive, setIsActive] = useState('');
  const [errors, setErrors] = useState<ReviewErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  useEffect(() => {
    if (!state?.firstName) navigate('/organisation-admin/users/create', { replace: true });
  }, [state, navigate]);

  const { mutate, isLoading } = useMutation(
    () =>
      createOrganisationUser(organisationId, {
        firstName,
        lastName,
        email,
        role,
        status: isActive === 'true' ? 'active' : 'deactivated',
      }),
    {
      onSuccess: () => {
        const userFullName = `${firstName} ${lastName}`.trim();

        queryClient.invalidateQueries(organisationUsersListKey(organisationId));
        navigate('/organisation-admin/users', {
          replace: true,
          state: {
            banner: {
              title: 'User added',
              body: `${userFullName} has been added to your list. You can view, edit or delete the user at any time.`,
            },
          },
        });
      },
      onError: (error: any) => {
        const status = error?.response?.status;
        setErrors({
          submit: status === 409 ? 'A user with this email address already exists.' : error?.response?.data?.toString() ?? 'Could not create the user. Try again.',
        });
      },
    }
  );

  if (!state?.firstName) return <></>;

  const validate = (): ReviewErrors => {
    const next: ReviewErrors = {};
    if (!isActive) next.isActive = 'Select whether to activate the account now or leave it deactivated';
    return next;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    mutate();
  };

  const errorList = Object.entries(errors)
    .filter(([, v]) => Boolean(v))
    .map(([key, text]) => ({ targetName: key, text: text ?? '' }));

  return (
    <OrganisationManagementLayout backLink={{ href: '/organisation-admin/users/create', onClick: () => navigate('/organisation-admin/users/create', { state }) }}>
      {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

      <span className="govuk-caption-l">Add user</span>
      <H1>Check your answers</H1>

      <form onSubmit={handleSubmit} noValidate>
        <GovUKSummaryCard
          title="User details"
          actions={[
            {
              label: 'Edit',
              onClick: () => navigate('/organisation-admin/users/create', { state }),
              visuallyHiddenText: 'user details',
              testId: 'organisation-admin-users-create-review-edit',
            },
          ]}
          testId="organisation-admin-users-create-review-summary">
          <SummaryList
            testId="organisation-admin-users-create-review-details"
            items={[
              { label: 'First name', value: firstName },
              { label: 'Last name', value: lastName },
              { label: 'Access level', value: roleName },
              {
                label: 'Email address',
                value: (
                  <a href={`mailto:${email}`} className="govuk-link" data-testid="organisation-admin-users-create-review-email-link">
                    {email}
                  </a>
                ),
              },
            ]}
          />
        </GovUKSummaryCard>

        <GovUKFieldset.RadioGroup
          name="isActive"
          legend="When do you want to activate this account?"
          legendSize="m"
          value={isActive}
          options={[
            {
              value: 'true',
              text: 'Activate now',
              hint: 'The user will be able to sign in to the service straight away',
            },
            {
              value: 'false',
              text: 'Leave deactivated',
              hint: 'You can activate the account at any time',
            },
          ]}
          error={errors.isActive}
          onChange={(v) => setIsActive(v)}
          testId="organisation-admin-users-create-review-activate"
        />

        <GovUKButton type="submit" isLoading={isLoading} disabled={isLoading} data-testid="organisation-admin-users-create-review-submit">
          Save user and finish
        </GovUKButton>
      </form>
    </OrganisationManagementLayout>
  );
};

export default UsersCreateReview;
