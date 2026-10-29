import React, { useCallback, useLayoutEffect, useState } from 'react';
import { httpDataOf } from '../../../../helpers/httpErrors';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKButton, ErrorSummary, H1, Paragraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { laFundingDashboardKey, localAuthoritiesListKey, localAuthorityKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { formatGBP } from '../../../../helpers/currency';
import { currentFinancialYear, financialYearOptions, nextFinancialYear } from '../../../../helpers/financialYear';
import { getLocalAuthority, type LocalAuthorityDto } from '../queries';
import { amendLocalAuthorityAllocation, type AmendLocalAuthorityAllocationPayload } from '../mutations';
import { formatAnnualAllocation, handleAnnualAllocationInput, removeAnnualAllocationFormatting, validateAnnualAllocation } from '../allocation';
import { organisationServerErrors } from '../errors';

// organisationServerErrors buckets anything it cannot route onto a field under `submit`, and the error summary
// links to that key by id. Named rather than a bare "submit" to match the sibling review screens, and because a
// generic id is easy to collide with.
const SUBMIT_BUTTON_ID = 'admin-local-authorities-amend-allocation-submit';

interface FormErrors {
  financialYear?: string;
  allocatedAmount?: string;
  nextYearAllocatedAmount?: string;
  submit?: string;
}

// The response carries only two years, so anything else is genuinely unknown here and the boxes go empty
// rather than guessing.
const knownAllocationFor = (year: string, dto: LocalAuthorityDto): number | null => {
  if (year && year === dto.fundingFinancialYear) return dto.allocatedAmount;
  if (year && year === dto.nextYearFinancialYear) return dto.nextYearAllocatedAmount;
  return null;
};

const LocalAuthoritiesAmendAllocation = (): React.ReactElement => {
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data, isLoading: loadingOrg } = useQuery({ queryKey: localAuthorityKey(organisationId), queryFn: () => getLocalAuthority(organisationId), enabled: !!organisationId });
  const original = data?.data;

  const [financialYear, setFinancialYear] = useState('');
  const [allocatedAmount, setAllocatedAmount] = useState('');
  const [nextYearAllocatedAmount, setNextYearAllocatedAmount] = useState('');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Amend allocation`);

  // The upcoming year is derived from the year selected above, never chosen separately, so the two amounts
  // cannot name the same year or leave a gap between them (BLUM-7608).
  const upcomingFinancialYear = nextFinancialYear(financialYear);

  // The amounts are per financial year, and the API upserts against whichever year is selected. So retargeting
  // the select has to re-seed both boxes, or the year on the label and the figure underneath it describe
  // different things and Save writes the wrong number to the newly-named year.
  //
  // useCallback with no deps: it touches only state setters and module-level functions, so its identity never
  // changes. That is what lets the layout effect below name it as a dependency. An unmemoised function there would
  // re-run the effect after every render and re-seed the boxes from `original`, undoing each keystroke.
  const applyFinancialYear = useCallback((year: string, dto: LocalAuthorityDto) => {
    const amount = knownAllocationFor(year, dto);
    const upcomingAmount = knownAllocationFor(nextFinancialYear(year), dto);

    setFinancialYear(year);
    setAllocatedAmount(amount == null ? '' : formatAnnualAllocation(String(amount)));
    setNextYearAllocatedAmount(upcomingAmount == null ? '' : formatAnnualAllocation(String(upcomingAmount)));
  }, []);

  const selectedYearAllocation = original ? knownAllocationFor(financialYear, original) : null;

  // financialYearOptions() spans the current year -1 to +4, and an organisation onboarded before that window
  // holds a year outside it. A select whose value matches no option silently falls back to the first one on
  // screen while state still says otherwise, so the year already recorded is folded in.
  const yearOptions = (() => {
    const years = new Set(financialYearOptions().map((option) => option.value));
    if (original?.fundingFinancialYear) years.add(original.fundingFinancialYear);
    if (original?.nextYearFinancialYear) years.add(original.nextYearFinancialYear);
    return [...years].sort().map((year) => ({ value: year, label: year }));
  })();

  // useLayoutEffect, not useEffect: the loading guard below stops gating the moment `original` arrives, so a
  // passive effect would leave one painted frame with no year selected - blank labels reading
  // "Annual allocation for  (£)" and a select matching none of its options.
  useLayoutEffect(() => {
    if (!original) return;
    applyFinancialYear(original.fundingFinancialYear ?? currentFinancialYear(), original);
  }, [original, applyFinancialYear]);

  const { mutate, isPending } = useMutation({
    mutationFn: (payload: AmendLocalAuthorityAllocationPayload) => amendLocalAuthorityAllocation(organisationId, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: localAuthoritiesListKey() });
      queryClient.invalidateQueries({ queryKey: localAuthorityKey(organisationId) });
      // The LA's own funding dashboard reads the same allocation, and used to keep showing the pre-amend
      // figure until its stale time expired.
      queryClient.invalidateQueries({ queryKey: laFundingDashboardKey });
      navigate(ROUTES.admin.organisations.detail(organisationId));
    },
    onError: (error) => {
      setErrors(organisationServerErrors(httpDataOf(error), 'Could not amend the allocation. Try again.'));
    },
  });

  const validate = (): FormErrors => {
    // Keys are inserted in the order the fields are rendered: the error summary is built from
    // Object.entries(errors) and GDS requires it to match the visual order of the form.
    const next: FormErrors = {};

    if (!financialYear.trim()) next.financialYear = 'Select a financial year';

    const allocationError = validateAnnualAllocation(allocatedAmount, 'annual allocation', true);
    if (allocationError) next.allocatedAmount = allocationError;

    // Optional: DHSC may not have agreed the upcoming year's figure yet.
    const nextYearError = validateAnnualAllocation(nextYearAllocatedAmount, `annual allocation for ${upcomingFinancialYear}`, false);
    if (nextYearError) next.nextYearAllocatedAmount = nextYearError;

    return next;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0) return;

    const trimmedNextYear = removeAnnualAllocationFormatting(nextYearAllocatedAmount.trim());
    mutate({
      allocatedAmount: Number(removeAnnualAllocationFormatting(allocatedAmount.trim())),
      financialYear,
      nextYearAllocatedAmount: trimmedNextYear ? Number(trimmedNextYear) : null,
    });
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key === 'submit' ? SUBMIT_BUTTON_ID : key, text: msg ?? '' }));

  if (loadingOrg || !original) {
    return (
      <GeneralLayout>
        <PageLoading />
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout
      breadcrumbs={[
        { label: 'Home', link: ROUTES.admin.home },
        { label: 'Local Authorities', link: ROUTES.admin.organisations.list },
        { label: original.name, link: ROUTES.admin.organisations.detail(organisationId) },
      ]}
      backLink={{ href: ROUTES.admin.organisations.detail(organisationId) }}>
      <div style={{ paddingTop: '20px' }}>
        <ErrorSummary heading="There is a problem" errors={errorList} testId="admin-local-authorities-amend-allocation-error-summary" focusTrigger={submitAttempts} />

        <H1>Amend allocation for {original.name}</H1>

        {/* Tracks the selected year rather than the organisation's latest one, so it never contradicts the
            figure in the box below it. */}
        <Paragraph>
          {selectedYearAllocation == null ? (
            <>
              No annual allocation is recorded for <strong>{financialYear}</strong> yet.
            </>
          ) : (
            <>
              Annual allocation currently recorded for {financialYear}: <strong>{formatGBP(selectedYearAllocation)}</strong>
            </>
          )}
        </Paragraph>

        <form onSubmit={handleSubmit} noValidate>
          {/* Above the amounts because it labels them: both inputs name the year they belong to. */}
          <GovUKFieldset.Select
            id="financialYear"
            name="financialYear"
            label="Financial year"
            hint="The year the annual allocation below applies to. Allocations already recorded against other years are kept."
            value={financialYear}
            options={yearOptions}
            error={errors.financialYear}
            onChange={(e) => applyFinancialYear(e.target.value, original)}
            required
            testId="admin-local-authorities-amend-allocation-financial-year"
          />

          <GovUKFieldset.Input
            id="allocatedAmount"
            name="allocatedAmount"
            label={`Annual allocation for ${financialYear} (£)`}
            hint="This replaces the allocation currently recorded for the financial year selected above."
            value={allocatedAmount}
            error={errors.allocatedAmount}
            inputMode="decimal"
            pattern="^[\d,]+(\.\d{0,2})?$"
            onChange={(e) => handleAnnualAllocationInput(e.target.value, setAllocatedAmount)}
            required
            testId="admin-local-authorities-amend-allocation-allocated-amount"
          />

          <GovUKFieldset.Input
            id="nextYearAllocatedAmount"
            name="nextYearAllocatedAmount"
            label={`Annual allocation for ${upcomingFinancialYear} (£)`}
            hint="Optional. Leave blank to keep whatever is already recorded for that year."
            value={nextYearAllocatedAmount}
            error={errors.nextYearAllocatedAmount}
            inputMode="decimal"
            pattern="^[\d,]+(\.\d{0,2})?$"
            onChange={(e) => handleAnnualAllocationInput(e.target.value, setNextYearAllocatedAmount)}
            testId="admin-local-authorities-amend-allocation-next-year-allocated-amount"
          />

          <div className="govuk-button-group">
            <GovUKButton id={SUBMIT_BUTTON_ID} type="submit" isLoading={isPending} disabled={isPending} data-testid={SUBMIT_BUTTON_ID}>
              Save allocation
            </GovUKButton>
            <a className="govuk-link" href={ROUTES.admin.organisations.detail(organisationId)} data-testid="admin-local-authorities-amend-allocation-cancel">
              Cancel
            </a>
          </div>
        </form>
      </div>
    </GeneralLayout>
  );
};

export default LocalAuthoritiesAmendAllocation;
