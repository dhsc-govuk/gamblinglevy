import React, { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { ManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { ROUTES } from '../../../../helpers/routes';
import { E_CODE_MAX_LENGTH, ONS_CODE_PATTERN, REGIONS } from '../queries';
import { formatAnnualAllocation, handleAnnualAllocationInput, removeAnnualAllocationFormatting, validateAnnualAllocation } from '../allocation';
import { currentFinancialYear, financialYearOptions, nextFinancialYear } from '../../../../helpers/financialYear';
import type { CreateOrganisationContactsState } from './Contacts';
import './styles.css';

const SUBMIT_BUTTON_ID = 'admin-organisations-create-submit';

// Shared with Review.tsx so the "Check your answers" row can never drift from the option text below it.
export const ACTIVATE_IMMEDIATELY_LABELS: Record<'now' | 'later', string> = {
  now: 'Activate it now',
  later: 'Leave it deactivated',
};

interface FormErrors {
  name?: string;
  onsCode?: string;
  region?: string;
  type?: string;
  eCode?: string;
  financialYear?: string;
  allocatedAmount?: string;
  nextYearAllocatedAmount?: string;
  activateImmediately?: string;
  submit?: string;
}

export interface CreateOrganisationDetailsState {
  name: string;
  onsCode: string;
  region: string;
  type: 'LA' | 'VCSE';
  eCode: string;
  financialYear: string;
  allocatedAmount: string;
  nextYearAllocatedAmount: string;
  // The answer to "When do you want to activate this account?" (BLUM-7945): '' until chosen.
  activateImmediately: 'now' | 'later' | '';
}

const LocalAuthoritiesCreate = (): React.ReactElement => {
  const navigate = useNavigate();
  const location = useLocation();
  const routeState = location.state as CreateOrganisationDetailsState | CreateOrganisationContactsState | null;
  const previousState = routeState && 'organisation' in routeState ? routeState.organisation : routeState;

  const [name, setName] = useState(previousState?.name ?? '');
  const [onsCode, setOnsCode] = useState(previousState?.onsCode ?? '');
  const [region, setRegion] = useState(previousState?.region ?? '');
  const [type, setType] = useState<'LA' | 'VCSE'>(previousState?.type ?? 'LA');
  const [eCode, setECode] = useState(previousState?.eCode ?? '');
  const [financialYear, setFinancialYear] = useState(previousState?.financialYear ?? currentFinancialYear());
  const [allocatedAmount, setAllocatedAmount] = useState(previousState?.allocatedAmount ? formatAnnualAllocation(previousState.allocatedAmount) : '');
  const [nextYearAllocatedAmount, setNextYearAllocatedAmount] = useState(
    previousState?.nextYearAllocatedAmount ? formatAnnualAllocation(previousState.nextYearAllocatedAmount) : ''
  );
  const [activateImmediately, setActivateImmediately] = useState<'now' | 'later' | ''>(previousState?.activateImmediately ?? '');

  // The upcoming year is derived, never chosen: two independent selects could name the same year or leave a
  // gap between them, and DHSC's requirement is specifically "this year and the one after" (BLUM-7608).
  const upcomingFinancialYear = nextFinancialYear(financialYear);
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Organisation details`);

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    // Keys are inserted in the order the fields are rendered, because the error summary is built from
    // Object.entries(errors) and GDS requires it to match the visual order of the form.
    if (!name.trim()) next.name = 'Enter the organisation name';
    if (!region.trim()) next.region = "Select the organisation's region";
    if (type !== 'LA' && type !== 'VCSE') next.type = 'Choose LA or VCSE';
    if (type === 'LA' && !onsCode.trim()) next.onsCode = 'Enter the ONS code';
    else if (type === 'LA' && onsCode.trim() && !ONS_CODE_PATTERN.test(onsCode.trim()))
      next.onsCode = 'ONS code is not recognised. Enter it in the format E06000001 (a letter followed by 8 numbers).';
    // Required for both organisation types, unlike the ONS code above (BLUM-7050).
    if (!eCode.trim()) next.eCode = 'Enter the E-code';
    else if (eCode.trim().length > E_CODE_MAX_LENGTH) next.eCode = `E-code must be ${E_CODE_MAX_LENGTH} characters or fewer`;

    if (!financialYear.trim()) next.financialYear = 'Select a financial year';

    const allocationError = validateAnnualAllocation(allocatedAmount, 'annual allocation', true);
    if (allocationError) next.allocatedAmount = allocationError;

    // Optional: DHSC may not know next year's figure at onboarding, so a blank field is not an error.
    const nextYearError = validateAnnualAllocation(nextYearAllocatedAmount, `annual allocation for ${upcomingFinancialYear}`, false);
    if (nextYearError) next.nextYearAllocatedAmount = nextYearError;

    if (!activateImmediately) next.activateImmediately = 'Select when you want to activate this account';

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
    if (Object.keys(next).length > 0) return;

    const nextOrganisationState = {
      name: name.trim(),
      // Derived here, not just in handleTypeChange: a user can pick VCSE first and type a code after,
      // which never re-fires that handler. This boundary is the one every path goes through.
      onsCode: type === 'LA' ? onsCode.trim() : '',
      region,
      type,
      eCode: eCode.trim(),
      financialYear,
      allocatedAmount: removeAnnualAllocationFormatting(allocatedAmount.trim()),
      nextYearAllocatedAmount: removeAnnualAllocationFormatting(nextYearAllocatedAmount.trim()),
      activateImmediately,
    } satisfies CreateOrganisationDetailsState;

    navigate(ROUTES.admin.organisations.createContacts, {
      state:
        routeState && 'contacts' in routeState
          ? ({ organisation: nextOrganisationState, contacts: routeState.contacts } satisfies CreateOrganisationContactsState)
          : nextOrganisationState,
    });
  };

  const targetForError = (key: string): string => {
    if (key === 'type') return 'type-LA';
    if (key === 'activateImmediately') return 'activateImmediately-now';
    // organisationServerErrors buckets anything it cannot route onto a field under `submit`; without this the
    // summary link points at an id nothing renders, so activating it moves no focus at all.
    if (key === 'submit') return SUBMIT_BUTTON_ID;
    return key;
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: targetForError(key), text: msg ?? '' }));

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.organisations.list }}>
      <div className="organisations-create-page">
        <ErrorSummary heading="There is a problem" errors={errorList} testId="admin-organisations-create-error-summary" focusTrigger={submitAttempts} />

        <div className="organisations-create-page__heading">
          <span className="govuk-caption-l">Add organisation</span>
          <H1>Organisation details</H1>
        </div>

        <form onSubmit={handleSubmit} noValidate>
          <div className="organisations-create-page__fields">
            <GovUKFieldset.Input
              id="name"
              name="name"
              label="Name"
              value={name}
              error={errors.name}
              onChange={(e) => setName(e.target.value)}
              required
              testId="admin-organisations-create-name"
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
              testId="admin-organisations-create-region"
            />
            <GovUKFieldset.RadioGroup
              name="type"
              legend="Type"
              legendSize="s"
              value={type}
              options={[
                { value: 'LA', text: 'Local Authority' },
                { value: 'VCSE', text: 'VCSE' },
              ]}
              error={errors.type}
              onChange={(value) => handleTypeChange(value as 'LA' | 'VCSE')}
              required
              testId="admin-organisations-create-type"
            />
            {/* Sits below Type because it is only required for Local Authorities — the question that
                determines the requirement has to be asked first. */}
            <GovUKFieldset.Input
              id="onsCode"
              name="onsCode"
              label="ONS code"
              hint="Required for Local Authorities. Format example: E06000001"
              value={onsCode}
              error={errors.onsCode}
              onChange={(e) => setOnsCode(e.target.value)}
              required={type === 'LA'}
              testId="admin-organisations-create-ons-code"
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
              testId="admin-organisations-create-e-code"
            />
            {/* Above the amounts because it labels them: both inputs name the year they belong to. */}
            <GovUKFieldset.Select
              id="financialYear"
              name="financialYear"
              label="Financial year"
              hint="The year the annual allocation below applies to."
              value={financialYear}
              options={financialYearOptions()}
              error={errors.financialYear}
              onChange={(e) => setFinancialYear(e.target.value)}
              required
              testId="admin-organisations-create-financial-year"
            />
            <GovUKFieldset.Input
              id="allocatedAmount"
              name="allocatedAmount"
              label={`Annual allocation for ${financialYear} (£)`}
              hint="Levy allocation for the financial year selected above."
              value={allocatedAmount}
              error={errors.allocatedAmount}
              inputMode="decimal"
              pattern="^[\d,]+(\.\d{0,2})?$"
              onChange={(e) => handleAnnualAllocationInput(e.target.value, setAllocatedAmount)}
              required
              testId="admin-organisations-create-allocated-amount"
            />
            <GovUKFieldset.Input
              id="nextYearAllocatedAmount"
              name="nextYearAllocatedAmount"
              label={`Annual allocation for ${upcomingFinancialYear} (£)`}
              hint="Optional. Leave blank if the upcoming year's allocation has not been agreed yet - it can be added later."
              value={nextYearAllocatedAmount}
              error={errors.nextYearAllocatedAmount}
              inputMode="decimal"
              pattern="^[\d,]+(\.\d{0,2})?$"
              onChange={(e) => handleAnnualAllocationInput(e.target.value, setNextYearAllocatedAmount)}
              testId="admin-organisations-create-next-year-allocated-amount"
            />
            <GovUKFieldset.RadioGroup
              name="activateImmediately"
              legend="When do you want to activate this account?"
              legendSize="s"
              value={activateImmediately}
              options={[
                { value: 'now', text: ACTIVATE_IMMEDIATELY_LABELS.now, hint: 'The user will be able to sign in to the service straight away' },
                { value: 'later', text: ACTIVATE_IMMEDIATELY_LABELS.later, hint: 'You can activate the account at any time' },
              ]}
              error={errors.activateImmediately}
              onChange={(value) => setActivateImmediately(value as 'now' | 'later')}
              required
              testId="admin-organisations-create-activate-immediately"
            />
          </div>

          <GovUKButton id={SUBMIT_BUTTON_ID} type="submit" data-testid={SUBMIT_BUTTON_ID}>
            Continue
          </GovUKButton>
        </form>
      </div>
    </ManagementLayout>
  );
};

export default LocalAuthoritiesCreate;
