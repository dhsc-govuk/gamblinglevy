import React, { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { httpDataOf } from '../../../../helpers/httpErrors';
import { GeneralLayout } from '../../../../layouts';
import { GovUKButton, GovUKErrorSummary, GovUKFieldset, GovUKWarningText, H1, H2, Paragraph, GovUKTag, GovUKTableScroll } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import AccessLevelGuidance from '../../../../components/Global/OrganisationUsers/AccessLevelGuidance';
import { composeTestId } from '../../../../helpers/testIds';
import { bulkImportLocalAuthorities, downloadBulkImportTemplate, BulkImportAdminUserOutcome, BulkImportResultDto, BulkImportRowOutcomeDto } from '../mutations';
import { organisationServerErrors } from '../errors';

// Each of these must stay in sync with the id rendered on the control it names: the error summary links to
// `#{targetName}`, so a value with no matching DOM id gives the user a link that goes nowhere.
const FILE_INPUT_ID = 'csv-file';
const DOWNLOAD_BUTTON_ID = 'download-template';

// Root the results table's testids derive from, following the GovUKTable convention: `{root}-row-{key}` and
// `{root}-row-{key}-cell-{column}`.
const RESULT_TEST_ID = 'admin-local-authorities-bulk-import-result';

const UPLOAD_FAILED_MESSAGE = 'CSV import failed. Check the file and try again.';
const TEMPLATE_FAILED_MESSAGE = 'Could not download the CSV template. Try again.';

// organisationServerErrors already covers every body this page can receive — the { error, message }
// envelope, a bare string, ASP.NET ValidationProblemDetails, a Blob, and an empty body. Only its `submit`
// key is reachable here (this command has no validator, so its field-mapped keys never fire). The trailing
// `|| fallback` is load-bearing: the helper uses `??` internally, so a `{ message: '' }` body returns an
// empty string, which would render a summary whose link has no text — the summary shows whenever `error` is
// non-null, not when the message is non-empty.
const errorMessageFrom = (e: unknown, fallback: string): string => organisationServerErrors(httpDataOf(e), fallback).submit || fallback;

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

const adminOutcomeColour = (outcome: BulkImportAdminUserOutcome) => {
  if (outcome === 'Created') return 'green';
  if (outcome === 'AlreadyExists') return 'blue';
  if (outcome === 'NotAttempted') return 'yellow';
  return 'red';
};

// The wire values are enum names; these are the words on the page. NotProvided has no entry on purpose: a row
// that asked for no admin has no status, and GDS reserves tags for things that do have one — see the render.
const ADMIN_OUTCOME_LABELS: Record<Exclude<BulkImportAdminUserOutcome, 'NotProvided'>, string> = {
  NotAttempted: 'Not attempted',
  Created: 'Created',
  AlreadyExists: 'Already exists',
  Failed: 'Failed',
};

// What a screen reader hears when an import finishes. Leads on the users that were not attempted, because
// that is the only outcome needing a second action from the admin — everything else is a readout they can
// take at their own pace from the table.
const importAnnouncement = (result: BulkImportResultDto): string => {
  const plural = (count: number, noun: string) => `${count} ${noun}${count === 1 ? '' : 's'}`;

  const summary =
    `Import complete. ${plural(result.createdCount, 'organisation')} created, ${result.updatedCount} updated, ` +
    `${result.failedCount} failed. ${plural(result.adminUsersCreatedCount, 'LA user')} created.`;

  return result.adminUsersNotAttemptedCount > 0
    ? `${summary} The import ran out of time before it could create ${result.adminUsersNotAttemptedCount} more. Upload the same file again to create the rest.`
    : summary;
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

  const { mutate, isPending } = useMutation({
    mutationFn: (f: File) => bulkImportLocalAuthorities(f),
    onSuccess: (response) => {
      setResult(response.data);
      queryClient.invalidateQueries({ queryKey: localAuthoritiesListKey() });
    },
    onError: (e) => {
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
        <GovUKErrorSummary
          heading="There is a problem"
          errors={error ? [{ targetName: error.targetName, text: error.message }] : []}
          focusTrigger={errorFocusTrigger}
          testId="admin-local-authorities-bulk-import-error-summary"
        />

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

        <Paragraph>
          To create the organisation&rsquo;s first user at the same time, fill in <code>la_admin_first_name</code>, <code>la_admin_last_name</code>,{' '}
          <code>la_admin_access_level</code> and <code>la_admin_email</code>. These four columns are optional: leave them all blank and only the organisation is imported. The user
          is emailed an invitation, and an email address already in use is reported without creating a second account.
        </Paragraph>

        {/* The two labels below are a contract with AccessLevelAliases in BulkImportLocalAuthoritiesCommand.cs,
            which keys off exactly these spellings. Nothing fails if they drift — the importer simply starts
            rejecting the words this page told the admin to type. Rendered from AccessLevelGuidance so the CSV
            and the Add user form always describe the access levels the same way. */}
        <Paragraph>
          Use <code>LA Admin</code> or <code>LA Lead</code> for <code>la_admin_access_level</code>, matched without regard to capitalisation.
        </Paragraph>
        <AccessLevelGuidance view="la" variant="details" testId="admin-local-authorities-bulk-import-access-level-guidance" />

        <GovUKButton
          type="button"
          id={DOWNLOAD_BUTTON_ID}
          className="govuk-button govuk-button--secondary"
          onClick={handleDownloadTemplate}
          data-testid="admin-local-authorities-bulk-import-download-template">
          Download CSV template
        </GovUKButton>

        <form onSubmit={handleSubmit} style={{ marginTop: '20px' }} noValidate>
          <GovUKFieldset.FileUpload
            id={FILE_INPUT_ID}
            name={FILE_INPUT_ID}
            label="CSV file"
            labelSize="none"
            accept=".csv,text/csv"
            error={error?.targetName === FILE_INPUT_ID ? error.message : undefined}
            onChange={setFile}
            testId="admin-local-authorities-bulk-import-file"
          />
          <GovUKButton type="submit" isLoading={isPending} disabled={isPending} data-testid="admin-local-authorities-bulk-import-submit">
            Upload and import
          </GovUKButton>
        </form>

        {/* Permanently mounted, and deliberately outside the results container below: a live region inserted
            into the DOM in the same commit as its own content announces nothing. Same reasoning, and same
            shape, as the organisations status region in Admin/Users/Create. handleSubmit clears `result`
            before each upload, so the next success is a genuine content change the region fires on. */}
        <div className="govuk-visually-hidden" role="status" aria-live="polite" aria-atomic="true" data-testid={composeTestId(RESULT_TEST_ID, 'status')}>
          {result ? importAnnouncement(result) : ''}
        </div>

        {result && (
          <div style={{ marginTop: '30px' }} data-testid={RESULT_TEST_ID}>
            <H2>Import summary</H2>
            <Paragraph>
              Created: <strong data-testid={composeTestId(RESULT_TEST_ID, 'created-count')}>{result.createdCount}</strong>
              {' · '}
              Updated: <strong data-testid={composeTestId(RESULT_TEST_ID, 'updated-count')}>{result.updatedCount}</strong>
              {' · '}
              Skipped: <strong data-testid={composeTestId(RESULT_TEST_ID, 'skipped-count')}>{result.skippedCount}</strong>
              {' · '}
              Failed: <strong data-testid={composeTestId(RESULT_TEST_ID, 'failed-count')}>{result.failedCount}</strong>
              {' · '}
              LA users created: <strong data-testid={composeTestId(RESULT_TEST_ID, 'admin-users-created-count')}>{result.adminUsersCreatedCount}</strong>
              {' · '}
              LA users failed: <strong data-testid={composeTestId(RESULT_TEST_ID, 'admin-users-failed-count')}>{result.adminUsersFailedCount}</strong>
              {' · '}
              LA users not attempted: <strong data-testid={composeTestId(RESULT_TEST_ID, 'admin-users-not-attempted-count')}>{result.adminUsersNotAttemptedCount}</strong>
            </Paragraph>

            {/* The one outcome that needs a second action from the admin, and the counts alone do not say so.
                Without this the only way to find out that users were skipped is to read every row. */}
            {result.adminUsersNotAttemptedCount > 0 && (
              <GovUKWarningText data-testid={composeTestId(RESULT_TEST_ID, 'admin-users-not-attempted-warning')}>
                The import ran out of time before it could create {result.adminUsersNotAttemptedCount} LA user
                {result.adminUsersNotAttemptedCount === 1 ? '' : 's'}. Upload the same file again to create the rest. The LA Admin result column shows which rows were not
                attempted.
              </GovUKWarningText>
            )}
            <GovUKTableScroll label="Import summary" testId={composeTestId(RESULT_TEST_ID, 'table-scroll')}>
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
                      LA Admin email
                    </th>
                    <th className="govuk-table__header" scope="col">
                      LA Admin result
                    </th>
                    <th className="govuk-table__header" scope="col">
                      Reason
                    </th>
                  </tr>
                </thead>
                <tbody className="govuk-table__body">
                  {result.rows.map((row) => {
                    const rowTestId = composeTestId(RESULT_TEST_ID, 'row', row.rowNumber);
                    // 'NotProvided' means the row asked for no admin, so there is no status to tag — the cell
                    // shows a dash like the email cell beside it. Every other value is a real outcome.
                    const adminOutcome = row.adminUserOutcome ?? 'NotProvided';

                    return (
                      <tr key={`${row.rowNumber}-${row.onsCode ?? row.name ?? ''}`} className="govuk-table__row" data-testid={rowTestId}>
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
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'admin-user-email')}>
                          {row.adminUserEmail ?? '—'}
                        </td>
                        {/* The admin's reason lives here rather than in the Reason column so it inherits this
                          header: merged under one "Reason" heading, a screen reader announces both strings
                          with no cue as to which is about the organisation and which about the user. */}
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'admin-user-outcome')}>
                          {adminOutcome === 'NotProvided' ? (
                            '—'
                          ) : (
                            <>
                              <GovUKTag colour={adminOutcomeColour(adminOutcome)} data-testid={composeTestId(rowTestId, 'admin-user-outcome-tag')}>
                                {ADMIN_OUTCOME_LABELS[adminOutcome]}
                              </GovUKTag>
                              {row.adminUserReason && (
                                <span className="govuk-!-display-block govuk-!-margin-top-1" data-testid={composeTestId(rowTestId, 'admin-user-reason')}>
                                  {row.adminUserReason}
                                </span>
                              )}
                            </>
                          )}
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

export default LocalAuthoritiesBulkImport;
