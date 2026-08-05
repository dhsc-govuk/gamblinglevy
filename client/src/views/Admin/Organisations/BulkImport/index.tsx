import React, { useState } from 'react';
import { useMutation, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { GovUKButton, GovUKErrorSummary, H1, H2, Paragraph, GovUKTag } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { bulkImportLocalAuthorities, downloadBulkImportTemplate, BulkImportResultDto, BulkImportRowOutcomeDto } from '../mutations';
import { organisationServerErrors } from '../errors';

// Each of these must stay in sync with the id rendered on the control it names: the error summary links to
// `#{targetName}`, so a value with no matching DOM id gives the user a link that goes nowhere.
const FILE_INPUT_ID = 'csv-file';
const DOWNLOAD_BUTTON_ID = 'download-template';

const UPLOAD_FAILED_MESSAGE = 'CSV import failed. Check the file and try again.';
const TEMPLATE_FAILED_MESSAGE = 'Could not download the CSV template. Try again.';

// organisationServerErrors already covers every body this page can receive — the { error, message }
// envelope, a bare string, ASP.NET ValidationProblemDetails, a Blob, and an empty body. Only its `submit`
// key is reachable here (this command has no validator, so its field-mapped keys never fire). The trailing
// `|| fallback` is load-bearing: the helper uses `??` internally, so a `{ message: '' }` body returns an
// empty string, which would render a summary whose link has no text — the summary shows whenever `error` is
// non-null, not when the message is non-empty.
const errorMessageFrom = (e: any, fallback: string): string => organisationServerErrors(e?.response?.data, fallback).submit || fallback;

interface PageError {
  message: string;
  // Which control the error summary's link should focus — a download failure must not send the user to the
  // file input.
  targetName: string;
}

const outcomeColour = (outcome: BulkImportRowOutcomeDto['outcome']) => {
  if (outcome === 'Created') return 'green';
  if (outcome === 'Updated') return 'blue';
  if (outcome === 'Skipped') return 'grey';
  return 'red';
};

const LocalAuthoritiesBulkImport = (): React.ReactElement => {
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<BulkImportResultDto | null>(null);
  const [error, setError] = useState<PageError | null>(null);
  // Bumped on every failed submit so the summary re-takes focus even when the message is unchanged.
  const [errorFocusTrigger, setErrorFocusTrigger] = useState(0);

  usePageTitle(`${error ? 'Error: ' : ''}Bulk import Local Authorities`);

  const showError = (message: string, targetName = FILE_INPUT_ID) => {
    setError({ message, targetName });
    setErrorFocusTrigger((count) => count + 1);
  };

  const { mutate, isLoading } = useMutation((f: File) => bulkImportLocalAuthorities(f), {
    onSuccess: (response) => {
      setResult(response.data);
      queryClient.invalidateQueries(localAuthoritiesListKey());
    },
    onError: (e: any) => {
      showError(errorMessageFrom(e, UPLOAD_FAILED_MESSAGE));
    },
  });

  const handleDownloadTemplate = async () => {
    setError(null);
    try {
      const response = await downloadBulkImportTemplate();
      const url = window.URL.createObjectURL(response.data);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'local-authorities-template.csv';
      a.click();
      window.URL.revokeObjectURL(url);
    } catch (e) {
      // Without this the request rejects into an unhandled promise and the button silently does nothing.
      showError(errorMessageFrom(e, TEMPLATE_FAILED_MESSAGE), DOWNLOAD_BUTTON_ID);
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setResult(null);
    if (!file) {
      showError('Choose a CSV file to upload.');
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
        {error && (
          <GovUKErrorSummary
            heading="There is a problem"
            errors={[{ targetName: error.targetName, text: error.message }]}
            focusTrigger={errorFocusTrigger}
            testId="admin-local-authorities-bulk-import-error-summary"
          />
        )}

        <H1>Bulk import Local Authorities</H1>

        <Paragraph>
          Upload a CSV with one row per Local Authority. Columns required: <code>name, region, type, e_code, allocated_amount</code>, plus <code>ons_code</code> for Local Authority
          rows. Rows are matched by ONS code: an organisation with a matching code is updated in place, and any other row creates a new organisation, so it is rejected if that name
          already exists.
        </Paragraph>

        <Paragraph>
          Use <code>LA</code> (or <code>Local Authority</code>) or <code>VCSE</code> for <code>type</code>, and an English region name such as <code>London</code> or{' '}
          <code>North West</code> for <code>region</code>. Both are matched without regard to capitalisation.
        </Paragraph>

        <GovUKButton
          type="button"
          id={DOWNLOAD_BUTTON_ID}
          className="govuk-button govuk-button--secondary"
          onClick={handleDownloadTemplate}
          data-testid="admin-local-authorities-bulk-import-download-template">
          Download CSV template
        </GovUKButton>

        <form onSubmit={handleSubmit} style={{ marginTop: '20px' }} noValidate>
          <div className="govuk-form-group">
            <label className="govuk-label" htmlFor={FILE_INPUT_ID}>
              CSV file
            </label>
            <input
              id={FILE_INPUT_ID}
              className="govuk-file-upload"
              type="file"
              accept=".csv,text/csv"
              data-testid="admin-local-authorities-bulk-import-file"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
          </div>
          <GovUKButton type="submit" isLoading={isLoading} disabled={isLoading} data-testid="admin-local-authorities-bulk-import-submit">
            Upload and import
          </GovUKButton>
        </form>

        {result && (
          <div style={{ marginTop: '30px' }} data-testid="admin-local-authorities-bulk-import-result">
            <H2>Import summary</H2>
            <Paragraph>
              Created: <strong>{result.createdCount}</strong>
              {' · '}
              Updated: <strong>{result.updatedCount}</strong>
              {' · '}
              Skipped: <strong>{result.skippedCount}</strong>
              {' · '}
              Failed: <strong>{result.failedCount}</strong>
            </Paragraph>
            <table className="govuk-table">
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
                {result.rows.map((row) => (
                  <tr key={`${row.rowNumber}-${row.onsCode ?? row.name ?? ''}`} className="govuk-table__row">
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

export default LocalAuthoritiesBulkImport;
