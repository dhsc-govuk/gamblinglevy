import React, { useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useLocation, useNavigate } from 'react-router-dom';
import { OrganisationManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import { ROLE_LABELS, laAdminAssignableRoles, useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { validateEmailAddress } from '../../../../helpers/validators';

export interface OrgCreateFormState {
  firstName: string;
  lastName: string;
  email: string;
  role: string;
  roleName: string;
  organisationId: string;
}

interface FormErrors {
  firstName?: string;
  lastName?: string;
  role?: string;
  email?: string;
}

const ACCESS_LEVEL_OPTIONS = [{ value: '', label: 'Select access level' }, ...laAdminAssignableRoles.map((r) => ({ value: r, label: ROLE_LABELS[r] ?? r }))];

const UsersCreate = (): React.ReactElement => {
  usePageTitle('Add user');
  const navigate = useNavigate();
  const location = useLocation();
  const { organisationId } = useAuthProvider();
  const initialState = location.state as OrgCreateFormState | null;

  const [firstName, setFirstName] = useState(initialState?.firstName ?? '');
  const [lastName, setLastName] = useState(initialState?.lastName ?? '');
  const [role, setRole] = useState<string>(initialState?.role ?? '');
  const [email, setEmail] = useState(initialState?.email ?? '');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  if (!organisationId) return <PageLoading />;

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    if (!firstName.trim()) next.firstName = "Enter the user's first name";
    if (!lastName.trim()) next.lastName = "Enter the user's last name";
    if (!role) next.role = 'Select an access level';
    if (!email.trim()) {
      next.email = "Enter the user's email address";
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
    const state: OrgCreateFormState = {
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      email: email.trim().toLowerCase(),
      role,
      roleName: ROLE_LABELS[role] ?? role,
      organisationId,
    };
    navigate('/organisation-admin/users/create/review', { state });
  };

  const errorList = Object.entries(errors)
    .filter(([, v]) => Boolean(v))
    .map(([key, text]) => ({ targetName: key, text: text ?? '' }));

  return (
    <OrganisationManagementLayout backLink={{ href: '/organisation-admin/users' }}>
      {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

      <span className="govuk-caption-l">Add user</span>
      <H1>User details</H1>

      <form onSubmit={handleSubmit} noValidate>
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
          id="role"
          name="role"
          label="Access level"
          value={role}
          options={ACCESS_LEVEL_OPTIONS}
          onChange={(e) => setRole(e.target.value)}
          error={errors.role}
          required
        />
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

        <GovUKButton type="submit" data-testid="organisation-admin-users-create-continue">
          Continue
        </GovUKButton>
      </form>
    </OrganisationManagementLayout>
  );
};

export default UsersCreate;
