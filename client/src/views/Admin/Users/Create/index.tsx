import React, { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, GovUKInsetText, GovUKWarningText, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import { AccessLevelGuidance } from '../../../../components/Global/OrganisationUsers';
import usePageTitle from '../../../../hooks/usePageTitle';
import { defaultStaleTime, localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { validateEmailAddress } from '../../../../helpers/validators';
import { getLocalAuthorities } from '../../Organisations/queries';
import { accessLevelOptionsFor, organisationRequiredFor } from '../accessLevels';
import useAccessLevelClamp from '../useAccessLevelClamp';
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

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Add user`);

  // Changing the organisation is the only place the organisation/role pair can go out of step, so the clamp
  // lives in the handler rather than an effect — an effect would also fire on the draft restored from
  // location.state and undo it.
  const { accessLevelNotice, handleOrganisationChange, handleRoleChange } = useAccessLevelClamp({ role, setRole, setOrganisationId, setErrors });

  const { data: organisationsData, isError: organisationsFailed } = useQuery({
    queryKey: localAuthoritiesListKey(),
    queryFn: () => getLocalAuthorities(),
    staleTime: defaultStaleTime,
    refetchOnWindowFocus: false,
  });

  const organisations = organisationsData?.data ?? [];

  const organisationOptions = [{ value: '', label: 'Select organisation' }, ...organisations.map((o) => ({ value: o.id, label: o.name }))];
  const accessLevelOptions = accessLevelOptionsFor(organisationId, role);

  // The access levels are derived from the organisation, so an unusable organisation list silently reduces the
  // form to "Super Admin only" — the LA roles simply stop being reachable. Say why rather than let a DHSC admin
  // conclude the LA roles have been withdrawn.
  //
  // Three states, not two, following views/Account/Sessions: react-query keeps `data` when a refetch fails,
  // so `isError` on its own is true while a populated list is on screen. Warning that only a Super Admin can be
  // created *there* would be simply untrue, and it would point the admin at the promotion path the conditional
  // dropdown exists to keep them off.
  const organisationsUnavailable = organisationsFailed && organisations.length === 0;
  const organisationsStale = organisationsFailed && organisations.length > 0;
  const noOrganisationsExist = !organisationsFailed && !!organisationsData && organisations.length === 0;

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    if (!firstName.trim()) next.firstName = 'Enter the user’s first name';
    if (!lastName.trim()) next.lastName = 'Enter the user’s last name';
    // Insertion order is the error-summary order, so it must match the order the fields appear on the page.
    if (organisationRequiredFor(role) && !organisationId) next.organisationId = 'Select an organisation';
    if (!role) next.role = 'Select an access level';
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
        <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

        <div className="users-create-page__heading">
          <span className="govuk-caption-l">Add user</span>
          <H1>User details</H1>
        </div>

        {/* Permanently mounted: the query can fail after first paint, and a live region inserted alongside its
            own text announces nothing. */}
        <div role="status" aria-live="polite" aria-atomic="true" data-testid="admin-users-create-organisations-status">
          {organisationsUnavailable && (
            <GovUKWarningText data-testid="admin-users-create-organisations-unavailable">
              The list of organisations could not be loaded, so only a Super Admin account can be created. Reload the page to try again.
            </GovUKWarningText>
          )}
          {organisationsStale && (
            <GovUKWarningText data-testid="admin-users-create-organisations-stale">
              We could not check for changes to the list of organisations just now, so it may be out of date. Reload the page to see the latest.
            </GovUKWarningText>
          )}
          {noOrganisationsExist && (
            <GovUKInsetText data-testid="admin-users-create-no-organisations">No organisations have been added yet, so only a Super Admin account can be created.</GovUKInsetText>
          )}
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
              hint="Select the organisation from the list. Leave blank to create a Super Admin user. Changing the organisation resets the access level."
              value={organisationId}
              options={organisationOptions}
              error={errors.organisationId}
              onChange={(e) => handleOrganisationChange(e.target.value)}
            />
            <div className="users-create-page__access-level">
              {/* The select and its status region share one flex item so the region contributes no `gap` while
                  it is empty. It stays mounted either way: a live region inserted in the same commit as its
                  text is the classic way to make an announcement that never happens. */}
              <div className="users-create-page__access-level-field">
                <GovUKFieldset.Select
                  id="role"
                  name="role"
                  label="Access level"
                  value={role}
                  options={accessLevelOptions}
                  error={errors.role}
                  onChange={(e) => handleRoleChange(e.target.value)}
                  required
                />
                <div role="status" aria-live="polite" aria-atomic="true" data-testid="admin-users-create-access-level-status">
                  {accessLevelNotice && <GovUKInsetText data-testid="admin-users-create-access-level-status-message">{accessLevelNotice}</GovUKInsetText>}
                </div>
              </div>
              <AccessLevelGuidance view="dhsc" variant="details" testId="admin-users-create-access-level-guidance" />
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
