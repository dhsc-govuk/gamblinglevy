import React, { useEffect, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle, { pageTitleWithName } from '../../../../hooks/usePageTitle';
import { localAuthorityKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { E_CODE_MAX_LENGTH, getLocalAuthority, ONS_CODE_PATTERN, REGIONS } from '../queries';
import type { EditOrganisationContactsState } from './Contacts';
import './styles.css';

interface FormErrors {
  name?: string;
  onsCode?: string;
  region?: string;
  type?: string;
  eCode?: string;
}

export interface EditOrganisationDetailsState {
  organisationId: string;
  name: string;
  onsCode: string;
  region: string;
  type: 'LA' | 'VCSE';
  eCode: string;
  originalOnsCode: string;
}

const LocalAuthoritiesEdit = (): React.ReactElement => {
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const location = useLocation();

  const routeState = location.state as EditOrganisationDetailsState | EditOrganisationContactsState | null;
  const incomingState = routeState && 'organisation' in routeState ? routeState.organisation : routeState;
  const hasIncomingState = !!incomingState;

  const { data, isLoading: loadingOrg } = useQuery({ queryKey: localAuthorityKey(organisationId), queryFn: () => getLocalAuthority(organisationId), enabled: !!organisationId });
  const original = data?.data;

  const [name, setName] = useState(incomingState?.name ?? '');
  const [onsCode, setOnsCode] = useState(incomingState?.onsCode ?? '');
  const [region, setRegion] = useState(incomingState?.region ?? '');
  const [type, setType] = useState<'LA' | 'VCSE'>(incomingState?.type ?? 'LA');
  const [eCode, setECode] = useState(incomingState?.eCode ?? '');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}${pageTitleWithName('Edit organisation', original?.name)}`);

  useEffect(() => {
    if (original && !hasIncomingState) {
      setName(original.name);
      setOnsCode(original.onsCode ?? '');
      setRegion(original.region);
      setType(original.type);
      setECode(original.eCode ?? '');
    }
    // Only seed from the fetched record when the user hasn't already brought edits back from a later step.
  }, [original, hasIncomingState]);

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    // Keys are inserted in the order the fields are rendered, because the error summary is built from
    // Object.entries(errors) and GDS requires it to match the visual order of the form.
    if (!name.trim()) next.name = 'Enter the LA name';
    if (!region.trim()) next.region = "Select the organisation's region";
    if (type !== 'LA' && type !== 'VCSE') next.type = 'Choose LA or VCSE';
    if (type === 'LA' && !onsCode.trim()) next.onsCode = 'Enter the ONS code';
    else if (type === 'LA' && onsCode.trim() && !ONS_CODE_PATTERN.test(onsCode.trim()))
      next.onsCode = 'ONS code is not recognised. Enter it in the format E06000001 (a letter followed by 8 numbers).';
    // Required for both organisation types, unlike the ONS code above. Organisations created before BLUM-7050
    // have no E-code, so the first edit of one has to supply it.
    if (!eCode.trim()) next.eCode = 'Enter the E-code';
    else if (eCode.trim().length > E_CODE_MAX_LENGTH) next.eCode = `E-code must be ${E_CODE_MAX_LENGTH} characters or fewer`;
    return next;
  };

  // Switching away from Local Authority has to discard the code, not just stop requiring it: an ONS code is
  // an LA geography identifier, so leaving a typed value in state would persist it against a VCSE
  // organisation and could even collide with a real LA's code on the unique index.
  const handleTypeChange = (nextType: 'LA' | 'VCSE') => {
    setType(nextType);
    if (nextType !== 'LA') {
      setOnsCode('');
      // Delete the key rather than set it undefined: usePageTitle counts keys, so an undefined value would
      // leave the "Error: " prefix on a page that is no longer showing any error.
      setErrors((prev) => {
        if (!prev.onsCode) return prev;
        const next = { ...prev };
        delete next.onsCode;
        return next;
      });
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0 || !original) return;

    const nextOrganisationState: EditOrganisationDetailsState = {
      organisationId,
      name: name.trim(),
      // Derived here, not just in handleTypeChange: a user can pick VCSE first and type a code after,
      // which never re-fires that handler. This boundary is the one every path goes through.
      onsCode: type === 'LA' ? onsCode.trim() : '',
      region,
      type,
      eCode: eCode.trim(),
      originalOnsCode: original.onsCode ?? '',
    };

    navigate(ROUTES.admin.organisations.editContacts(organisationId), {
      state:
        routeState && 'contacts' in routeState
          ? ({ organisation: nextOrganisationState, contacts: routeState.contacts, removedContactIds: routeState.removedContactIds } satisfies EditOrganisationContactsState)
          : nextOrganisationState,
    });
  };

  const targetForError = (key: string): string => {
    if (key === 'type') return 'type-LA';
    return key;
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: targetForError(key), text: msg ?? '' }));

  if (loadingOrg || !original) {
    return (
      <ManagementLayout>
        <PageLoading />
      </ManagementLayout>
    );
  }

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.organisations.detail(organisationId) }}>
      <div className="organisations-edit-page">
        <ErrorSummary heading="There is a problem" errors={errorList} testId="admin-local-authorities-edit-error-summary" focusTrigger={submitAttempts} />

        <div className="organisations-edit-page__heading">
          <span className="govuk-caption-l">Edit organisation</span>
          <H1>{original.name}</H1>
        </div>

        <form onSubmit={handleSubmit} noValidate>
          <div className="organisations-edit-page__fields">
            <GovUKFieldset.Input
              id="name"
              name="name"
              label="Name"
              value={name}
              error={errors.name}
              onChange={(e) => setName(e.target.value)}
              required
              testId="admin-local-authorities-edit-name"
            />
            <GovUKFieldset.Select
              id="region"
              name="region"
              label="Region"
              value={region}
              options={[{ value: '', label: 'Select region' }, ...REGIONS.map((r) => ({ value: r, label: r }))]}
              error={errors.region}
              onChange={(e) => setRegion(e.target.value)}
              required
              testId="admin-local-authorities-edit-region"
            />
            <GovUKFieldset.RadioGroup
              name="type"
              legend="Type"
              value={type}
              options={[
                { value: 'LA', text: 'Local Authority' },
                { value: 'VCSE', text: 'VCSE' },
              ]}
              error={errors.type}
              onChange={(v) => handleTypeChange(v as 'LA' | 'VCSE')}
              required
              testId="admin-local-authorities-edit-type"
            />
            {/* Sits below Type because it is only required for Local Authorities — the question that
                determines the requirement has to be asked first. */}
            <GovUKFieldset.Input
              id="onsCode"
              name="onsCode"
              label="ONS code"
              hint="Required for Local Authorities. Format example: E06000001. Changing this affects the finance export linkage."
              value={onsCode}
              error={errors.onsCode}
              onChange={(e) => setOnsCode(e.target.value)}
              required={type === 'LA'}
              testId="admin-local-authorities-edit-ons-code"
            />
            <GovUKFieldset.Input
              id="eCode"
              name="eCode"
              label="E-code"
              hint="DHSC internal finance identifier. For example, E1234567."
              value={eCode}
              error={errors.eCode}
              onChange={(e) => setECode(e.target.value)}
              required
              testId="admin-local-authorities-edit-e-code"
            />
          </div>

          <GovUKButton type="submit" data-testid="admin-local-authorities-edit-submit">
            Continue
          </GovUKButton>
        </form>
      </div>
    </ManagementLayout>
  );
};

export default LocalAuthoritiesEdit;
