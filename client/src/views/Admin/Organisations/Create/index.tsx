import React, { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { ManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { ROUTES } from '../../../../helpers/routes';
import { E_CODE_MAX_LENGTH, REGIONS } from '../queries';
import type { CreateOrganisationContactsState } from './Contacts';
import './styles.css';

interface FormErrors {
  name?: string;
  onsCode?: string;
  region?: string;
  type?: string;
  eCode?: string;
  allocatedAmount?: string;
  submit?: string;
}

export interface CreateOrganisationDetailsState {
  name: string;
  onsCode: string;
  region: string;
  type: 'LA' | 'VCSE';
  eCode: string;
  allocatedAmount: string;
}

const annualAllocationPattern = /^\d+(?:\.\d{0,2})?$/;
const annualAllocationInputPattern = /^\d*(?:\.\d{0,2})?$/;

const removeAnnualAllocationFormatting = (value: string): string => value.replace(/,/g, '');

const formatAnnualAllocation = (value: string): string => {
  const [wholePart, decimalPart] = value.split('.');
  const formattedWholePart = wholePart.replace(/\B(?=(\d{3})+(?!\d))/g, ',');

  return decimalPart === undefined ? formattedWholePart : `${formattedWholePart}.${decimalPart}`;
};

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
  const [allocatedAmount, setAllocatedAmount] = useState(previousState?.allocatedAmount ? formatAnnualAllocation(previousState.allocatedAmount) : '');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Organisation details`);
  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  const validate = (): FormErrors => {
    const next: FormErrors = {};
    // Keys are inserted in the order the fields are rendered, because the error summary is built from
    // Object.entries(errors) and GDS requires it to match the visual order of the form.
    if (!name.trim()) next.name = 'Enter the organisation name';
    if (!region.trim()) next.region = 'Choose a region';
    if (type !== 'LA' && type !== 'VCSE') next.type = 'Choose LA or VCSE';
    if (type === 'LA' && !onsCode.trim()) next.onsCode = 'Enter the ONS code';
    // Required for both organisation types, unlike the ONS code above (BLUM-7050).
    if (!eCode.trim()) next.eCode = 'Enter the E-code';
    else if (eCode.trim().length > E_CODE_MAX_LENGTH) next.eCode = `E-code must be ${E_CODE_MAX_LENGTH} characters or fewer`;

    const trimmedAllocation = removeAnnualAllocationFormatting(allocatedAmount.trim());
    if (!trimmedAllocation) {
      next.allocatedAmount = 'Enter the annual allocation';
    } else if (!annualAllocationPattern.test(trimmedAllocation)) {
      next.allocatedAmount = 'Annual allocation must be a number with no more than 2 decimal places';
    } else if (Number.isNaN(Number(trimmedAllocation)) || Number(trimmedAllocation) <= 0) {
      next.allocatedAmount = 'Annual allocation must be a number greater than 0';
    }

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

  const handleAllocatedAmountChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const nextValue = removeAnnualAllocationFormatting(e.target.value);
    if (annualAllocationInputPattern.test(nextValue)) {
      setAllocatedAmount(formatAnnualAllocation(nextValue));
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
      allocatedAmount: removeAnnualAllocationFormatting(allocatedAmount.trim()),
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
    return key;
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: targetForError(key), text: msg ?? '' }));

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.organisations.list }}>
      <div className="organisations-create-page">
        {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} testId="admin-organisations-create-error-summary" />}

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
              options={[{ value: '', label: 'Choose region' }, ...REGIONS.map((r) => ({ value: r, label: r }))]}
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
              hint="DHSC internal finance identifier."
              value={eCode}
              error={errors.eCode}
              onChange={(e) => setECode(e.target.value)}
              required
              testId="admin-organisations-create-e-code"
            />
            <GovUKFieldset.Input
              id="allocatedAmount"
              name="allocatedAmount"
              label="Annual Allocation (£)"
              hint="Levy allocation for the current financial year."
              value={allocatedAmount}
              error={errors.allocatedAmount}
              inputMode="decimal"
              pattern="^[\d,]+(\.\d{0,2})?$"
              onChange={handleAllocatedAmountChange}
              required
              testId="admin-organisations-create-allocated-amount"
            />
          </div>

          <GovUKButton type="submit" data-testid="admin-organisations-create-submit">
            Continue
          </GovUKButton>
        </form>
      </div>
    </ManagementLayout>
  );
};

export default LocalAuthoritiesCreate;
