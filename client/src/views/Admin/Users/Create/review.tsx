import React, { useState, useEffect } from 'react';
import { Link as RouterLink, useNavigate, useLocation } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { httpMessageOf, httpStatusOf } from '../../../../helpers/httpErrors';
import { ManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, GovUKSummaryCard, SummaryList, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { adminUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { createUser, createOrganisationUser } from '../mutations';
import { ROLE_LABELS } from '../../../../components/AuthProvider';
import { type CreateUserFormState } from './index';
import { ROLE_ADMIN } from '../accessLevels';

interface ReviewErrors {
  isActive?: string;
  submit?: string;
}

const UsersCreateReview = (): React.ReactElement => {
  usePageTitle('Check your answers');
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();

  const state = location.state as CreateUserFormState | null;

  // Destructure with fallbacks so hooks below are never in a conditional branch.
  const firstName = state?.firstName ?? '';
  const lastName = state?.lastName ?? '';
  const role = state?.role ?? '';
  const organisationId = state?.organisationId ?? '';
  const organisationName = state?.organisationName ?? '';
  const email = state?.email ?? '';
  const isAdminRole = role === ROLE_ADMIN;

  const [isActive, setIsActive] = useState('');
  const [errors, setErrors] = useState<ReviewErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  // Redirect to the form if navigated here directly without state.
  useEffect(() => {
    if (!state?.firstName) {
      navigate(ROUTES.admin.users.create, { replace: true });
    }
  }, [state, navigate]);

  const onSuccess = () => {
    queryClient.invalidateQueries({ queryKey: adminUsersListKey });
    navigate(ROUTES.admin.users.list, {
      state: {
        banner: {
          title: 'User added',
          body: `${firstName} ${lastName} has been added to your list. You can view, edit or delete the user at any time.`,
        },
      },
    });
  };

  const onError = (error: unknown) => {
    const status = httpStatusOf(error);
    const message = httpMessageOf(error) ?? 'Could not create the user. Try again.';
    setErrors({ submit: status === 409 ? 'A user with this email address already exists.' : message });
  };

  const { mutate: mutateAdmin, isPending: isLoadingAdmin } = useMutation({ mutationFn: createUser, onSuccess, onError });
  const { mutate: mutateOrg, isPending: isLoadingOrg } = useMutation({ mutationFn: createOrganisationUser, onSuccess, onError });

  const isLoading = isLoadingAdmin || isLoadingOrg;

  // All hooks are above — safe to early-return now.
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

    if (isAdminRole) {
      mutateAdmin({ firstName, lastName, email, isActive: isActive === 'true' });
    } else {
      mutateOrg({ firstName, lastName, email, organisationId, role, isActive: isActive === 'true' });
    }
  };

  const summaryItems = [
    { label: 'First name', value: firstName },
    { label: 'Last name', value: lastName },
    ...(isAdminRole
      ? []
      : [
          {
            label: 'Organisation',
            value: (
              <RouterLink to={ROUTES.admin.organisations.detail(organisationId)} className="govuk-link" data-testid="admin-users-review-organisation-link">
                {organisationName}
              </RouterLink>
            ),
          },
        ]),
    { label: 'Access level', value: ROLE_LABELS[role] ?? role },
    {
      label: 'Email address',
      value: (
        <a href={`mailto:${email}`} className="govuk-link" data-testid="admin-users-review-email-link">
          {email}
        </a>
      ),
    },
  ];

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key, text: msg ?? '' }));

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.users.create, onClick: () => navigate(ROUTES.admin.users.create, { state }) }}>
      <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

      <span className="govuk-caption-l">Add user</span>
      <H1>Check your answers</H1>

      <form onSubmit={handleSubmit} noValidate>
        <GovUKSummaryCard
          title="User details"
          actions={[
            {
              label: 'Edit',
              onClick: () => navigate(ROUTES.admin.users.create, { state }),
              visuallyHiddenText: 'user details',
              testId: 'admin-users-review-summary-action-edit',
            },
          ]}
          testId="admin-users-review-summary">
          <SummaryList items={summaryItems} testId="admin-users-review-details" />
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
          testId="admin-users-review-activate"
        />

        <GovUKButton type="submit" isLoading={isLoading} disabled={isLoading} data-testid="admin-users-review-submit">
          Save user and finish
        </GovUKButton>
      </form>
    </ManagementLayout>
  );
};

export default UsersCreateReview;
