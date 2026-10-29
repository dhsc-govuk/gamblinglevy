import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { httpMessageOf, httpStatusOf } from '../../../helpers/httpErrors';
import { GeneralLayout } from '../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, H1 } from '../../GovUKComponents';
import { EnumUserRole, ROLE_LABELS, laAdminAssignableRoles } from '../../AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { organisationUsersListKey } from '../../../helpers/queriesParams';
import { validateEmailAddress } from '../../../helpers/validators';
import { createOrganisationUser } from './api';
import type { Breadcrumb } from './UsersListView';

interface FormErrors {
  firstName?: string;
  lastName?: string;
  email?: string;
  role?: string;
  submit?: string;
}

const roleOptions = laAdminAssignableRoles.map((role) => ({
  value: role,
  label: ROLE_LABELS[role] ?? role,
}));

export interface UserCreateViewProps {
  organisationId: string;
  pageTitle: string;
  heading: string;
  breadcrumbs: Breadcrumb[];
  backLinkHref: string;
  successNavigateFn: (newUserId: string) => string;
  testIdPrefix: string;
}

const UserCreateView = ({ organisationId, pageTitle, heading, breadcrumbs, backLinkHref, successNavigateFn, testIdPrefix }: UserCreateViewProps): React.ReactElement => {
  usePageTitle(pageTitle);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<string>(EnumUserRole.LEAD);
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { mutate, isPending } = useMutation({
    mutationFn: (payload: { firstName: string; lastName: string; email: string; role: string }) =>
      createOrganisationUser(organisationId, {
        ...payload,
        status: 'active',
      }),
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: organisationUsersListKey(organisationId) });
      navigate(successNavigateFn(response.data.id));
    },
    onError: (error) => {
      const status = httpStatusOf(error);
      const message = httpMessageOf(error) ?? 'Could not create the user. Try again.';
      if (status === 409) {
        setErrors({ email: 'A user with this email address already exists.' });
      } else if (status === 422) {
        setErrors({ submit: message });
      } else {
        setErrors({ submit: message });
      }
    },
  });

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    if (!firstName.trim()) next.firstName = 'Enter the user’s first name';
    if (!lastName.trim()) next.lastName = 'Enter the user’s last name';
    if (!email.trim()) {
      next.email = 'Enter the user’s email address';
    } else if (!validateEmailAddress(email.trim())) {
      next.email = 'Enter an email address in the correct format, like name@example.com';
    }
    if (!role) next.role = 'Select a role for the user';
    return next;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    mutate({
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      email: email.trim().toLowerCase(),
      role,
    });
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key, text: msg ?? '' }));

  return (
    <GeneralLayout breadcrumbs={breadcrumbs} backLink={{ href: backLinkHref }}>
      <div style={{ paddingTop: '20px' }}>
        <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

        <H1>{heading}</H1>

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
          <GovUKFieldset.Input
            id="email"
            name="email"
            label="Email address"
            type="email"
            value={email}
            error={errors.email}
            onChange={(e) => setEmail(e.target.value)}
            onBlur={() => setEmail((v) => v.trim().toLowerCase())}
            autoComplete="email"
            required
          />
          <GovUKFieldset.Select id="role" name="role" label="Role" value={role} options={roleOptions} onChange={(e) => setRole(e.target.value)} error={errors.role} required />

          <GovUKButton type="submit" isLoading={isPending} disabled={isPending} data-testid={`${testIdPrefix}-create-submit`}>
            Create user
          </GovUKButton>
        </form>
      </div>
    </GeneralLayout>
  );
};

export default UserCreateView;
