import React, { useEffect, useRef, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, useParams } from 'react-router-dom';
import { OrganisationManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, NotAuthorised, H1 } from '../../../../components/GovUKComponents';
import { ROLE_LABELS, laAdminAssignableRoles, useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { validateEmailAddress } from '../../../../helpers/validators';
import { UserLoadFailed, useOrganisationUser, organisationUserPageTitle } from '../../../../components/Global/OrganisationUsers';

export interface OrgEditFormState {
  userId: string;
  organisationId: string;
  firstName: string;
  lastName: string;
  email: string;
  role: string;
  roleName: string;
  status: 'active' | 'deactivated';
  originalFullName: string;
}

interface FormErrors {
  firstName?: string;
  lastName?: string;
  role?: string;
  email?: string;
}

const ACCESS_LEVEL_OPTIONS = laAdminAssignableRoles.map((r) => ({
  value: r,
  label: ROLE_LABELS[r] ?? r,
}));

const UsersEdit = (): React.ReactElement => {
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const { organisationId } = useAuthProvider();

  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [role, setRole] = useState('');
  const [email, setEmail] = useState('');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const userQuery = useOrganisationUser(userId);
  const { user, isDenied, loadFailed, isLoading } = userQuery;

  // Seed the form once per record, not on every response object. Keying this on `data` meant any successful background
  // refetch — a reconnect, a StrictMode remount — silently snapped the fields back to the server values and threw away
  // what the admin had typed.
  const hydratedUserId = useRef<string | null>(null);
  useEffect(() => {
    if (!user || hydratedUserId.current === user.id) return;
    hydratedUserId.current = user.id;
    setFirstName(user.firstName);
    setLastName(user.lastName);
    setEmail(user.email);
    setRole(user.role);
  }, [user]);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  usePageTitle(organisationUserPageTitle(userQuery, 'Edit user'));

  // Reached by hand-editing the URL to another LA's user id. NotAuthorised renders its own <main>, so it replaces the
  // layout rather than sitting inside it — otherwise the page would carry two main landmarks.
  if (isDenied) return <NotAuthorised goBack={navigate} testId="organisation-admin-users-edit-not-authorised" />;

  if (loadFailed) {
    return (
      <OrganisationManagementLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
        <UserLoadFailed testId="organisation-admin-users-edit-load-error" />
      </OrganisationManagementLayout>
    );
  }

  if (!organisationId) return <PageLoading />;

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    if (!firstName.trim()) next.firstName = "Enter the user's first name";
    if (!lastName.trim()) next.lastName = "Enter the user's last name";
    if (!role) next.role = 'Select an access level';
    if (!email.trim()) {
      next.email = "Enter the user's email address";
    } else if (!validateEmailAddress(email.trim())) {
      next.email = 'Enter a valid email address';
    }
    return next;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    const state: OrgEditFormState = {
      userId,
      organisationId,
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      email: email.trim().toLowerCase(),
      role,
      roleName: ROLE_LABELS[role] ?? role,
      status: user?.status ?? 'active',
      originalFullName: `${user?.firstName ?? ''} ${user?.lastName ?? ''}`.trim(),
    };
    navigate(`/organisation-admin/users/${userId}/edit/review`, { state });
  };

  const errorList = Object.entries(errors)
    .filter(([, v]) => Boolean(v))
    .map(([key, text]) => ({ targetName: key, text: text ?? '' }));

  return (
    <OrganisationManagementLayout backLink={{ href: `/organisation-admin/users/${userId}` }}>
      {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

      <span className="govuk-caption-l">Edit user</span>
      <H1>User details</H1>

      {isLoading ? (
        <PageLoading />
      ) : (
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

          <GovUKButton type="submit" data-testid="organisation-admin-users-edit-continue">
            Continue
          </GovUKButton>
        </form>
      )}
    </OrganisationManagementLayout>
  );
};

export default UsersEdit;
