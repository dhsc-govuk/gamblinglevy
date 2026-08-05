import React, { useState } from 'react';
import { useMutation, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import {
  GovUKButton,
  ErrorSummary,
  H1,
  H2,
  Paragraph,
  GovUKTag,
} from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import {
  bulkAmendAllocations,
  downloadBulkAmendAllocationsTemplate,
  BulkImportResultDto,
  BulkImportRowOutcomeDto,
} from '../mutations';

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

  const { mutate, isLoading } = useMutation((f: File) => bulkAmendAllocations(f), {
    onSuccess: (response) => {
      setResult(response.data);
      queryClient.invalidateQueries(localAuthoritiesListKey());
    },
    onError: (e: any) => {
      const message = e?.response?.data?.toString() ?? 'CSV upload failed. Check the file and try again.';
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
      backLink={{ href: ROUTES.admin.organisations.list }}
    >
      <div style={{ paddingTop: '20px' }}>
        {error && <ErrorSummary heading="There is a problem" errors={[{ targetName: 'file', text: error }]} />}

        <H1>Bulk amend allocations</H1>

        <Paragraph>
          Upload a CSV with one row per Local Authority. Columns required:
          {' '}<code>ons_code, allocated_amount</code>. Existing allocations are amended in place; rows with an unrecognised ONS code are skipped.
        </Paragraph>

        <GovUKButton
          type="button"
          className="govuk-button govuk-button--secondary"
          onClick={handleDownloadTemplate}
          data-testid="admin-local-authorities-bulk-amend-allocations-download-template"
        >
          Download CSV template
        </GovUKButton>

        <form onSubmit={handleSubmit} style={{ marginTop: '20px' }} noValidate>
          <div className="govuk-form-group">
            <label className="govuk-label" htmlFor="csv-file">CSV file</label>
            <input
              id="csv-file"
              className="govuk-file-upload"
              type="file"
              accept=".csv,text/csv"
              data-testid="admin-local-authorities-bulk-amend-allocations-file"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
          </div>
          <GovUKButton
            type="submit"
            isLoading={isLoading}
            disabled={isLoading}
            data-testid="admin-local-authorities-bulk-amend-allocations-submit"
          >
            Upload and amend
          </GovUKButton>
        </form>

        {result && (
          <div style={{ marginTop: '30px' }} data-testid="admin-local-authorities-bulk-amend-allocations-result">
            <H2>Amendment summary</H2>
            <Paragraph>
              Updated: <strong>{result.updatedCount}</strong>{' · '}
              Skipped: <strong>{result.skippedCount}</strong>{' · '}
              Failed: <strong>{result.failedCount}</strong>
            </Paragraph>
            <table className="govuk-table">
              <thead className="govuk-table__head">
                <tr className="govuk-table__row">
                  <th className="govuk-table__header" scope="col">Row</th>
                  <th className="govuk-table__header" scope="col">Name</th>
                  <th className="govuk-table__header" scope="col">ONS code</th>
                  <th className="govuk-table__header" scope="col">Outcome</th>
                  <th className="govuk-table__header" scope="col">Reason</th>
                </tr>
              </thead>
              <tbody className="govuk-table__body">
                {result.rows.map((row) => (
                  <tr key={`${row.rowNumber}-${row.onsCode ?? ''}`} className="govuk-table__row">
                    <td className="govuk-table__cell">{row.rowNumber}</td>
                    <td className="govuk-table__cell">{row.name ?? '—'}</td>
                    <td className="govuk-table__cell">{row.onsCode ?? '—'}</td>
                    <td className="govuk-table__cell">
                      <GovUKTag colour={outcomeColour(row.outcome)}>{row.outcome}</GovUKTag>
                    </td>
                    <td className="govuk-table__cell">{row.reason ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </GeneralLayout>
  );
};

export default LocalAuthoritiesBulkAmendAllocations;
