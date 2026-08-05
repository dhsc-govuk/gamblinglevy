import React, { useEffect, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import {
  GovUKFieldset,
  GovUKButton,
  ErrorSummary,
  H1,
  Paragraph,
} from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { localAuthoritiesListKey, localAuthorityKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getLocalAuthority } from '../queries';
import { amendLocalAuthorityAllocation } from '../mutations';
import { organisationServerErrors } from '../errors';

interface FormErrors {
  allocatedAmount?: string;
  submit?: string;
}

const formatAllocation = (amount: number | null) =>
  amount === null ? '—' : `£${amount.toLocaleString('en-GB', { maximumFractionDigits: 2 })}`;

const LocalAuthoritiesAmendAllocation = (): React.ReactElement => {
  usePageTitle('Amend allocation');
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data, isLoading: loadingOrg } = useQuery(
    localAuthorityKey(organisationId),
    () => getLocalAuthority(organisationId),
    { enabled: !!organisationId },
  );
  const original = data?.data;

  const [allocatedAmount, setAllocatedAmount] = useState('');
  const [errors, setErrors] = useState<FormErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  useEffect(() => {
    if (original?.allocatedAmount != null) {
      setAllocatedAmount(String(original.allocatedAmount));
    }
  }, [original]);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  const { mutate, isLoading } = useMutation(
    (amount: number) => amendLocalAuthorityAllocation(organisationId, amount),
    {
      onSuccess: () => {
        queryClient.invalidateQueries(localAuthoritiesListKey());
        queryClient.invalidateQueries(localAuthorityKey(organisationId));
        navigate(ROUTES.admin.organisations.detail(organisationId));
      },
      onError: (error: any) => {
        setErrors(organisationServerErrors(error?.response?.data, 'Could not amend the allocation. Try again.'));
      },
    },
  );

  const validate = (): { errors: FormErrors; amount?: number } => {
    const next: FormErrors = {};
    const trimmed = allocatedAmount.trim();
    if (!trimmed) {
      next.allocatedAmount = 'Enter the new annual allocation';
      return { errors: next };
    }
    const parsed = Number(trimmed);
    if (!Number.isFinite(parsed) || parsed <= 0) {
      next.allocatedAmount = 'Annual allocation must be a number greater than 0';
      return { errors: next };
    }
    return { errors: next, amount: parsed };
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);
    const { errors: next, amount } = validate();
    setErrors(next);
    if (Object.keys(next).length > 0 || amount === undefined) return;
    mutate(amount);
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key, text: msg ?? '' }));

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
      backLink={{ href: ROUTES.admin.organisations.detail(organisationId) }}
    >
      <div style={{ paddingTop: '20px' }}>
        {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

        <H1>Amend allocation for {original.name}</H1>

        <Paragraph>
          Current annual allocation: <strong>{formatAllocation(original.allocatedAmount)}</strong>
          {original.fundingFinancialYear && <> ({original.fundingFinancialYear})</>}
        </Paragraph>

        <form onSubmit={handleSubmit} noValidate>
          <GovUKFieldset.Input
            id="allocatedAmount"
            name="allocatedAmount"
            label="New annual allocation (£)"
            hint="Enter the new levy allocation for this Local Authority. This will replace the current value."
            type="number"
            value={allocatedAmount}
            error={errors.allocatedAmount}
            onChange={(e) => setAllocatedAmount(e.target.value)}
            required
          />

          <div className="govuk-button-group">
            <GovUKButton
              type="submit"
              isLoading={isLoading}
              disabled={isLoading}
              data-testid="admin-local-authorities-amend-allocation-submit"
            >
              Save allocation
            </GovUKButton>
            <a
              className="govuk-link"
              href={ROUTES.admin.organisations.detail(organisationId)}
              data-testid="admin-local-authorities-amend-allocation-cancel"
            >
              Cancel
            </a>
          </div>
        </form>
      </div>
    </GeneralLayout>
  );
};

export default LocalAuthoritiesAmendAllocation;
