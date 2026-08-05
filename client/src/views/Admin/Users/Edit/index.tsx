import React, { useEffect, useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate, useParams } from 'react-router-dom';
import { useQuery } from 'react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { adminUserKey, defaultStaleTime, localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { validateEmailAddress } from '../../../../helpers/validators';
import { getUser } from '../queries';
import { getLocalAuthorities } from '../../Organisations/queries';
import { editAccessLevelOptions } from '../accessLevels';
import './styles.css';

interface FormErrors {
  firstName?: string;
  lastName?: string;
  role?: string;
  organisationId?: string;
  email?: string;
}

export interface EditUserFormState {
  userId: string;
  originalFullName: string;
  firstName: string;
  lastName: string;
  role: string;
  organisationId: string;
  organisationName: string;
  email: string;
}

const UsersEdit = (): React.ReactElement => {
  usePageTitle('Edit user');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const draftState = location.state as EditUserFormState | null;
  const hasDraft = draftState?.userId === userId;

  const [firstName, setFirstName] = useState(hasDraft ? draftState.firstName : '');
  const [lastName, setLastName] = useState(hasDraft ? draftState.lastName : '');
  const [role, setRole] = useState(hasDraft ? draftState.role : '');
  const [organisationId, setOrganisationId] = useState(hasDraft ? draftState.organisationId : '');
  const [email, setEmail] = useState(hasDraft ? draftState.email : '');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { data: userData } = useQuery(adminUserKey(userId), () => getUser(userId), { enabled: !!userId });
  const { data: organisationsData } = useQuery(localAuthoritiesListKey(), () => getLocalAuthorities(), {
    staleTime: defaultStaleTime,
    refetchOnWindowFocus: false,
  });

  useEffect(() => {
    if (hasDraft && draftState) {
      setFirstName(draftState.firstName);
      setLastName(draftState.lastName);
      setRole(draftState.role);
      setOrganisationId(draftState.organisationId);
      setEmail(draftState.email);
    } else if (userData?.data) {
      const u = userData.data;
      setFirstName(u.firstName);
      setLastName(u.lastName);
      setRole(u.role);
      setOrganisationId(u.organisationId ?? '');
      setEmail(u.email);
    }
  }, [draftState, hasDraft, userData]);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  const organisations = organisationsData?.data ?? [];
  const organisationOptions = [{ value: '', label: 'Select organisation' }, ...organisations.map((o) => ({ value: o.id, label: o.name }))];

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    if (!firstName.trim()) next.firstName = 'Enter the user’s first name';
    if (!lastName.trim()) next.lastName = 'Enter the user’s last name';
    if (!organisationId) next.organisationId = 'Select an organisation';
    if (!role) next.role = 'Select an access level';
    if (!email.trim()) {
      next.email = 'Enter the user’s email address';
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

    const selectedOrg = organisations.find((o) => o.id === organisationId);
    const state: EditUserFormState = {
      userId,
      originalFullName: draftState?.originalFullName ?? (userData?.data ? `${userData.data.firstName} ${userData.data.lastName}` : `${firstName.trim()} ${lastName.trim()}`),
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      role,
      organisationId,
      organisationName: selectedOrg?.name ?? (draftState?.organisationId === organisationId ? draftState.organisationName : ''),
      email: email.trim().toLowerCase(),
    };
    navigate(ROUTES.admin.users.editReview(userId), { state });
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key, text: msg ?? '' }));

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.users.detail(userId) }}>
      <div className="admin-users-edit-page">
        {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

        <div className="admin-users-edit-page__heading">
          <span className="govuk-caption-l">Edit user</span>
          <H1>User details</H1>
        </div>

        <form onSubmit={handleSubmit} noValidate>
          <div className="admin-users-edit-page__fields">
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
              hint="Type the name of the organisation, then select from the list"
              value={organisationId}
              options={organisationOptions}
              error={errors.organisationId}
              onChange={(e) => setOrganisationId(e.target.value)}
              required
            />
            <div className="admin-users-edit-page__access-level">
              <GovUKFieldset.Select
                id="role"
                name="role"
                label="Access level"
                value={role}
                options={editAccessLevelOptions()}
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

          <GovUKButton type="submit" data-testid="admin-users-edit-continue">
            Continue
          </GovUKButton>
        </form>
      </div>
    </ManagementLayout>
  );
};

export default UsersEdit;
