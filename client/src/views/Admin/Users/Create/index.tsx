import React, { useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import { useQuery } from 'react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { defaultStaleTime, localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { validateEmailAddress } from '../../../../helpers/validators';
import { getLocalAuthorities } from '../../Organisations/queries';
import { ROLE_ADMIN, createAccessLevelOptions } from '../accessLevels';
import './styles.css';

interface FormErrors {
  firstName?: string;
  lastName?: string;
  role?: string;
  organisationId?: string;
  email?: string;
}

export interface CreateUserFormState {
  firstName: string;
  lastName: string;
  role: string;
  organisationId: string;
  organisationName: string;
  email: string;
}

const UsersCreate = (): React.ReactElement => {
  usePageTitle('Add user');
  const navigate = useNavigate();
  const location = useLocation();
  const initialState = location.state as CreateUserFormState | null;

  const [firstName, setFirstName] = useState(initialState?.firstName ?? '');
  const [lastName, setLastName] = useState(initialState?.lastName ?? '');
  const [role, setRole] = useState(initialState?.role ?? '');
  const [organisationId, setOrganisationId] = useState(initialState?.organisationId ?? '');
  const [email, setEmail] = useState(initialState?.email ?? '');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  const { data: organisationsData } = useQuery(localAuthoritiesListKey(), () => getLocalAuthorities(), {
    staleTime: defaultStaleTime,
    refetchOnWindowFocus: false,
  });

  const organisations = organisationsData?.data ?? [];

  const organisationOptions = [{ value: '', label: 'Select organisation' }, ...organisations.map((o) => ({ value: o.id, label: o.name }))];
  const accessLevelOptions = createAccessLevelOptions();

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    if (!firstName.trim()) next.firstName = 'Enter the user’s first name';
    if (!lastName.trim()) next.lastName = 'Enter the user’s last name';
    if (!role) next.role = 'Select an access level';
    if (role !== ROLE_ADMIN && !organisationId) next.organisationId = 'Select an organisation';
    if (!email.trim()) {
      next.email = 'Enter the user’s email address';
    } else if (!validateEmailAddress(email.trim())) {
      next.email = 'Enter an email address in the correct format, like name@example.com';
    }
    return next;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0) return;

    const selectedOrg = organisations.find((o) => o.id === organisationId);
    const state: CreateUserFormState = {
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      role,
      organisationId,
      organisationName: selectedOrg?.name ?? '',
      email: email.trim().toLowerCase(),
    };
    navigate(ROUTES.admin.users.createReview, { state });
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key, text: msg ?? '' }));

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.users.list }}>
      <div className="users-create-page">
        {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

        <div className="users-create-page__heading">
          <span className="govuk-caption-l">Add user</span>
          <H1>User details</H1>
        </div>

        <form onSubmit={handleSubmit} noValidate>
          <div className="users-create-page__fields">
            <GovUKFieldset.Input
              id="firstName"
              name="firstName"
              label="First name"
              value={firstName}
              error={errors.firstName}
              onChange={(e) => setFirstName(e.target.value)}
              autoComplete="given-name"
              required
            />
            <GovUKFieldset.Input
              id="lastName"
              name="lastName"
              label="Last name"
              value={lastName}
              error={errors.lastName}
              onChange={(e) => setLastName(e.target.value)}
              autoComplete="family-name"
              required
            />
            <GovUKFieldset.Select
              id="organisationId"
              name="organisationId"
              label="Organisation"
              hint="Type the name of the organisation, then select from the list. Leave blank to create a Super Admin user."
              value={organisationId}
              options={organisationOptions}
              error={errors.organisationId}
              onChange={(e) => setOrganisationId(e.target.value)}
            />
            <div className="users-create-page__access-level">
              <GovUKFieldset.Select
                id="role"
                name="role"
                label="Access level"
                value={role}
                options={accessLevelOptions}
                error={errors.role}
                onChange={(e) => setRole(e.target.value)}
                required
              />
              <RouterLink to={ROUTES.admin.users.accessLevelTypes} className="govuk-link">
                Access level types
              </RouterLink>
            </div>
            <GovUKFieldset.Input
              id="email"
              name="email"
              label="Email address"
              hint="Enter their work email address, not a group or distribution email"
              type="email"
              value={email}
              error={errors.email}
              onChange={(e) => setEmail(e.target.value)}
              onBlur={() => setEmail((v) => v.trim().toLowerCase())}
              autoComplete="email"
              required
            />
          </div>

          <GovUKButton type="submit" data-testid="admin-users-create-continue">
            Continue
          </GovUKButton>
        </form>
      </div>
    </ManagementLayout>
  );
};

export default UsersCreate;
