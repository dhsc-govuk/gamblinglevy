import React, { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { httpMessageOf } from '../../../../helpers/httpErrors';
import { GeneralLayout } from '../../../../layouts';
import { GovUKButton, ErrorSummary, GovUKFieldset, H1, H2, Paragraph, GovUKTag, GovUKTableScroll } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { composeTestId } from '../../../../helpers/testIds';
import { bulkAmendAllocations, downloadBulkAmendAllocationsTemplate, BulkImportResultDto, BulkImportRowOutcomeDto } from '../mutations';

const RESULT_TABLE_TEST_ID = 'admin-local-authorities-bulk-amend-allocations-result-table';

const outcomeColour = (outcome: BulkImportRowOutcomeDto['outcome']) => {
  if (outcome === 'Created') return 'green';
  if (outcome === 'Updated') return 'blue';
  if (outcome === 'Skipped') return 'grey';
  return 'red';
};

const LocalAuthoritiesBulkAmendAllocations = (): React.ReactElement => {
  usePageTitle('Bulk amend allocations');
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<BulkImportResultDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { mutate, isPending } = useMutation({
    mutationFn: (f: File) => bulkAmendAllocations(f),
    onSuccess: (response) => {
      setResult(response.data);
      queryClient.invalidateQueries({ queryKey: localAuthoritiesListKey() });
    },
    onError: (e) => {
      const message = httpMessageOf(e) ?? 'CSV upload failed. Check the file and try again.';
      setError(message);
    },
  });

  const handleDownloadTemplate = async () => {
    const response = await downloadBulkAmendAllocationsTemplate();
    const url = window.URL.createObjectURL(response.data);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'local-authorities-allocations-template.csv';
    a.click();
    window.URL.revokeObjectURL(url);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);
    setError(null);
    setResult(null);
    if (!file) {
      setError('Choose a CSV file to upload.');
      return;
    }
    mutate(file);
  };

  return (
    <GeneralLayout
      breadcrumbs={[
        { label: 'Home', link: ROUTES.admin.home },
        { label: 'Local Authorities', link: ROUTES.admin.organisations.list },
      ]}
      backLink={{ href: ROUTES.admin.organisations.list }}>
      <div style={{ paddingTop: '20px' }}>
        <ErrorSummary heading="There is a problem" errors={error ? [{ targetName: 'csv-file', text: error }] : []} focusTrigger={submitAttempts} />

        <H1>Bulk amend allocations</H1>

        <Paragraph>
          Upload a CSV with one row per Local Authority. Columns required: <code>ons_code, allocated_amount</code>. Existing allocations are amended in place; rows with an
          unrecognised ONS code are skipped.
        </Paragraph>

        <GovUKButton
          type="button"
          className="govuk-button govuk-button--secondary"
          onClick={handleDownloadTemplate}
          data-testid="admin-local-authorities-bulk-amend-allocations-download-template">
          Download CSV template
        </GovUKButton>

        <form onSubmit={handleSubmit} style={{ marginTop: '20px' }} noValidate>
          <GovUKFieldset.FileUpload
            id="csv-file"
            name="csv-file"
            label="CSV file"
            labelSize="none"
            accept=".csv,text/csv"
            error={error ?? undefined}
            onChange={setFile}
            testId="admin-local-authorities-bulk-amend-allocations-file"
          />
          <GovUKButton type="submit" isLoading={isPending} disabled={isPending} data-testid="admin-local-authorities-bulk-amend-allocations-submit">
            Upload and amend
          </GovUKButton>
        </form>

        {result && (
          <div style={{ marginTop: '30px' }} data-testid="admin-local-authorities-bulk-amend-allocations-result">
            <H2>Amendment summary</H2>
            <Paragraph>
              Updated: <strong>{result.updatedCount}</strong>
              {' · '}
              Skipped: <strong>{result.skippedCount}</strong>
              {' · '}
              Failed: <strong>{result.failedCount}</strong>
            </Paragraph>
            <GovUKTableScroll label="Amendment summary" testId={composeTestId(RESULT_TABLE_TEST_ID, 'scroll')}>
              <table className="govuk-table" data-testid={RESULT_TABLE_TEST_ID}>
                <thead className="govuk-table__head">
                  <tr className="govuk-table__row">
                    <th className="govuk-table__header" scope="col">
                      Row
                    </th>
                    <th className="govuk-table__header" scope="col">
                      Name
                    </th>
                    <th className="govuk-table__header" scope="col">
                      ONS code
                    </th>
                    <th className="govuk-table__header" scope="col">
                      Outcome
                    </th>
                    <th className="govuk-table__header" scope="col">
                      Reason
                    </th>
                  </tr>
                </thead>
                <tbody className="govuk-table__body">
                  {result.rows.map((row) => {
                    const rowTestId = composeTestId(RESULT_TABLE_TEST_ID, 'row', row.rowNumber);
                    return (
                      <tr key={`${row.rowNumber}-${row.onsCode ?? ''}`} className="govuk-table__row" data-testid={rowTestId}>
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'row-number')}>
                          {row.rowNumber}
                        </td>
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'name')}>
                          {row.name ?? '—'}
                        </td>
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'ons-code')}>
                          {row.onsCode ?? '—'}
                        </td>
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'outcome')}>
                          <GovUKTag colour={outcomeColour(row.outcome)}>{row.outcome}</GovUKTag>
                        </td>
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'reason')}>
                          {row.reason ?? '—'}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </GovUKTableScroll>
          </div>
        )}
      </div>
    </GeneralLayout>
  );
};

export default LocalAuthoritiesBulkAmendAllocations;
