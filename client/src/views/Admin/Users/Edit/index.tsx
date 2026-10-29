import React, { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, GovUKInsetText, GovUKWarningText, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import { AccessLevelGuidance } from '../../../../components/Global/OrganisationUsers';
import usePageTitle from '../../../../hooks/usePageTitle';
import { adminUserKey, defaultStaleTime, localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { validateEmailAddress } from '../../../../helpers/validators';
import { getUser } from '../queries';
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
  const hydratedUserId = useRef<string | null>(null);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Edit user`);

  const { data: userData } = useQuery({ queryKey: adminUserKey(userId), queryFn: () => getUser(userId), enabled: !!userId, staleTime: defaultStaleTime });
  const { data: organisationsData, isError: organisationsFailed } = useQuery({
    queryKey: localAuthoritiesListKey(),
    queryFn: () => getLocalAuthorities(),
    staleTime: defaultStaleTime,
    refetchOnWindowFocus: false,
  });

  useEffect(() => {
    // Populate exactly once per user. The guard has to sit above *both* branches: this effect depends on
    // `userData`, whose identity changes on every refetch (react-query's structural sharing cannot preserve an
    // AxiosResponse — `response.request` is a fresh XMLHttpRequest each time), so either branch re-running
    // silently reverted whatever the admin had typed since. On the draft branch that also re-opened the
    // announcement bug: it writes `role` without going through handleRoleChange, so the notice could be left
    // holding its message while the role was restored underneath it, and the next clamp then set the identical
    // string, mutated no DOM, and announced nothing.
    if (hydratedUserId.current === userId) return;

    // A draft returned from the review page is the newer answer, so it wins over the server record. The
    // useState initialisers above already read it — this branch is what covers a draft arriving with the
    // server response still in flight.
    if (hasDraft && draftState) {
      hydratedUserId.current = userId;
      setFirstName(draftState.firstName);
      setLastName(draftState.lastName);
      setRole(draftState.role);
      setOrganisationId(draftState.organisationId);
      setEmail(draftState.email);
      return;
    }

    if (!userData?.data) return;
    hydratedUserId.current = userId;
    const u = userData.data;
    setFirstName(u.firstName);
    setLastName(u.lastName);
    setRole(u.role);
    setOrganisationId(u.organisationId ?? '');
    setEmail(u.email);
  }, [draftState, hasDraft, userData, userId]);

  const organisations = organisationsData?.data ?? [];
  // The user's own organisation is appended when the list does not contain it, for the same reason
  // accessLevelOptionsFor takes a currentRole: a native <select> renders a value absent from its options as
  // though nothing were chosen, so a failed list turned "this user belongs to Barnet" into a blank field — and
  // handleSubmit then resolved organisationName to '', so Check your answers showed "-" for a value it would keep.
  //
  // The name has to be *proven* to belong to the id being labelled. accessLevelOptionsFor's currentRole append
  // is safe because the value it inserts is the value it labels; a name is a separate fact. Pairing the live
  // organisationId with userData's name unchecked would, after a Back from Check your answers that changed the
  // organisation, label the new id with the old name — the select and the summary would both say Barnet while
  // the save moved the user to Camden.
  const persisted = userData?.data;
  const nameForCurrentOrganisation = (() => {
    if (!organisationId) return undefined;
    if (persisted?.organisationId === organisationId) return persisted.organisationName;
    // Gated on hasDraft so "when is draftState trustworthy" stays one rule. The id equality below would carry
    // the proof on its own, but only this keeps another user's draft out of the question entirely.
    return hasDraft && draftState.organisationId === organisationId ? draftState.organisationName : undefined;
  })();
  const organisationOptions = [
    { value: '', label: 'Select organisation' },
    ...organisations.map((o) => ({ value: o.id, label: o.name })),
    ...(nameForCurrentOrganisation && !organisations.some((o) => o.id === organisationId) ? [{ value: organisationId, label: nameForCurrentOrganisation }] : []),
  ];
  // `role` is passed so the select can always display the user's saved access level, even one the conditional
  // rule would not offer — a persisted LA role with no organisation is reachable when their LA was deleted.
  const accessLevelOptions = accessLevelOptionsFor(organisationId, role);

  // Changing the organisation is the only place the organisation/role pair can go out of step, so the clamp
  // lives in the handler rather than an effect — the hydration effect above restores a pair that is already
  // consistent, and an effect racing it would clear the role the user came to edit.
  const { accessLevelNotice, handleOrganisationChange, handleRoleChange } = useAccessLevelClamp({ role, setRole, setOrganisationId, setErrors });

  // Two states, not one: react-query keeps `data` when a refetch fails, so `isError` on its own is true while
  // a fully populated list is on screen — telling the admin the organisation "cannot be changed" there would be
  // untrue. Only the absence of a usable list actually blocks them. Mirrors views/Account/Sessions.
  const organisationsUnavailable = organisationsFailed && organisations.length === 0;
  const organisationsStale = organisationsFailed && organisations.length > 0;

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    if (!firstName.trim()) next.firstName = 'Enter the user’s first name';
    if (!lastName.trim()) next.lastName = 'Enter the user’s last name';
    // Conditionally required: a Super Admin has no organisation, so demanding one here made every existing
    // Super Admin unsavable — the admin was forced to pick an org the handler then discards (BLUM-7452).
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

    // Resolved from the same option list the user chose from, so an organisation only the persisted record knows
    // about still reaches Check your answers by name rather than as "-". Guarded on organisationId because the
    // list's first entry is the empty prompt — an unguarded lookup sends 'Select organisation' as the name.
    const selectedOrgName = organisationId ? organisationOptions.find((o) => o.value === organisationId)?.label : undefined;
    const state: EditUserFormState = {
      userId,
      originalFullName: draftState?.originalFullName ?? (userData?.data ? `${userData.data.firstName} ${userData.data.lastName}` : `${firstName.trim()} ${lastName.trim()}`),
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      role,
      organisationId,
      organisationName: selectedOrgName ?? (draftState?.organisationId === organisationId ? draftState.organisationName : ''),
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
        <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

        <div className="admin-users-edit-page__heading">
          <span className="govuk-caption-l">Edit user</span>
          <H1>User details</H1>
        </div>

        {/* Permanently mounted: the query can fail after first paint, and a live region inserted alongside its
            own text announces nothing. */}
        <div role="status" aria-live="polite" aria-atomic="true" data-testid="admin-users-edit-organisations-status">
          {organisationsUnavailable && (
            <GovUKWarningText data-testid="admin-users-edit-organisations-unavailable">
              The list of organisations could not be loaded, so the organisation cannot be changed. Reload the page to try again.
            </GovUKWarningText>
          )}
          {organisationsStale && (
            <GovUKWarningText data-testid="admin-users-edit-organisations-stale">
              We could not check for changes to the list of organisations just now, so it may be out of date. Reload the page to see the latest.
            </GovUKWarningText>
          )}
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
              hint="Select the organisation from the list. To make this user a Super Admin, choose “Select organisation”. Changing the organisation resets the access level."
              value={organisationId}
              options={organisationOptions}
              error={errors.organisationId}
              onChange={(e) => handleOrganisationChange(e.target.value)}
              required={organisationRequiredFor(role)}
            />
            <div className="admin-users-edit-page__access-level">
              {/* The select and its status region share one flex item so the region contributes no `gap` while
                  it is empty. It stays mounted either way: a live region inserted in the same commit as its
                  text is the classic way to make an announcement that never happens. */}
              <div className="admin-users-edit-page__access-level-field">
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
                <div role="status" aria-live="polite" aria-atomic="true" data-testid="admin-users-edit-access-level-status">
                  {accessLevelNotice && <GovUKInsetText data-testid="admin-users-edit-access-level-status-message">{accessLevelNotice}</GovUKInsetText>}
                </div>
              </div>
              <AccessLevelGuidance view="dhsc" variant="details" testId="admin-users-edit-access-level-guidance" />
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
