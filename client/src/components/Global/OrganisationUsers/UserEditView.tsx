import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { httpMessageOf, httpStatusOf } from '../../../helpers/httpErrors';
import { GeneralLayout } from '../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, H1 } from '../../GovUKComponents';
import { ROLE_LABELS, laAdminAssignableRoles } from '../../AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { organisationUserKey, organisationUsersListKey } from '../../../helpers/queriesParams';
import { validateEmailAddress } from '../../../helpers/validators';
import { getOrganisationUser, updateOrganisationUser } from './api';
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

export interface UserEditViewProps {
  organisationId: string;
  userId: string;
  pageTitle: string;
  heading: string;
  breadcrumbs: Breadcrumb[];
  backLinkHref: string;
  successNavigateHref: string;
  testIdPrefix: string;
}

const UserEditView = ({ organisationId, userId, pageTitle, heading, breadcrumbs, backLinkHref, successNavigateHref, testIdPrefix }: UserEditViewProps): React.ReactElement => {
  usePageTitle(pageTitle);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('');
  const [status, setStatus] = useState<'active' | 'deactivated'>('active');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { data, isLoading } = useQuery({
    queryKey: organisationUserKey(organisationId, userId),
    queryFn: () => getOrganisationUser(organisationId, userId),
    enabled: !!organisationId && !!userId,
  });

  useEffect(() => {
    if (data?.data) {
      setFirstName(data.data.firstName);
      setLastName(data.data.lastName);
      setEmail(data.data.email);
      setRole(data.data.role);
      setStatus(data.data.status);
    }
  }, [data]);

  const { mutate, isPending: isSaving } = useMutation({
    mutationFn: () =>
      updateOrganisationUser(organisationId, {
        id: userId,
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: email.trim().toLowerCase(),
        role,
        status,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: organisationUsersListKey(organisationId) });
      queryClient.invalidateQueries({ queryKey: organisationUserKey(organisationId, userId) });
      navigate(successNavigateHref);
    },
    onError: (error) => {
      const httpStatus = httpStatusOf(error);
      const message = httpMessageOf(error) ?? 'Could not update the user. Try again.';
      if (httpStatus === 409) {
        setErrors({ email: 'A user with this email address already exists.' });
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
    mutate();
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key, text: msg ?? '' }));

  return (
    <GeneralLayout breadcrumbs={breadcrumbs} backLink={{ href: backLinkHref }}>
      <div style={{ paddingTop: '20px' }}>
        <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

        <H1>{heading}</H1>

        {isLoading ? (
          <p className="govuk-body">Loading…</p>
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

            <GovUKButton type="submit" isLoading={isSaving} disabled={isSaving} data-testid={`${testIdPrefix}-edit-submit`}>
              Save changes
            </GovUKButton>
          </form>
        )}
      </div>
    </GeneralLayout>
  );
};

export default UserEditView;
