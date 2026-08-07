import React, { useEffect, useId, useMemo, useState } from 'react';
import { NotAuthorised, PageLoading } from '../../../../components/GovUKComponents';
import { useQuery, useMutation } from 'react-query';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { GeneralLayout } from '../../../../layouts';
import { GovUKWarningText, H1, H2, Paragraph, type FilterOption } from '../../../../components/GovUKComponents';
import { SearchIcon, SpreadsheetIcon } from '../../../../components/Icons';
import usePageTitle from '../../../../hooks/usePageTitle';
import { reportSubmissionsKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getReportSubmissions, type CollectionSubmissionSummaryDto, type OrgSubmissionStatusDto, type SubmissionStatus } from '../queries';
import { downloadWindowSubmissionsExport } from '../mutations';
import { formatGovUkDate, REPORT_COLLECTION_STATUS_LABEL } from '../display';
import { reportBreadcrumbs } from '../navigation';
import { reportFailureMessage, reportLoadVerdict, reportPageTitle, reportQueryRetry, REPORT_NOT_FOUND_MESSAGE } from '../messages';
import { downloadErrorMessage } from '../../../../helpers/downloadErrors';
import ReportLoadFailure from '../../../../components/Global/Reports/ReportLoadFailure';
import './styles.css';

const sortOptions: FilterOption[] = [
  { value: 'organisationName-asc', label: 'Name A-Z' },
  { value: 'organisationName-desc', label: 'Name Z-A' },
];

const statusFilterOptions: FilterOption[] = [
  { value: 'not_started', label: 'Not started' },
  { value: 'in_progress', label: 'Incomplete' },
  { value: 'completed', label: 'Completed' },
  { value: 'overdue', label: 'Overdue' },
];

const displayStatusLabel: Record<SubmissionStatus, string> = {
  not_started: 'Not started',
  in_progress: 'Incomplete',
  completed: 'Completed',
  overdue: 'Overdue',
};

const submittedLine = (org: OrgSubmissionStatusDto): string | null => {
  if (!org.submittedAt) {
    return null;
  }

  return `Submitted on ${formatGovUkDate(org.submittedAt)}`;
};

// The consolidated export's display name, dated by the REPORTING PERIOD — the same pair the per-organisation detail
// page names its download by, so the two DHSC screens cannot describe one collection differently (BLUM-7079).
//
// The quarter and dates are now read straight off the collection. They used to be guessed: the quarter by regexing
// "Q2"/"Quarter 2" out of the admin-entered collection *name* (falling back to the month the collection window
// opened, then to a hard-coded 4), and the dates from the window rather than the period. For a window that opens
// after the quarter it reports on — the normal shape whenever "window same as reporting period" is No — that named
// the wrong quarter over the wrong dates. Absent values now collapse the clause rather than emitting the
// "{quarterStartDate}" design placeholders, which a draft collection rendered into live link text.
const consolidatedReportTitle = (summary: CollectionSubmissionSummaryDto): string => {
  const quarterText = summary.quarter == null ? 'Consolidated quarterly report' : `Consolidated quarterly report for Quarter ${summary.quarter}`;
  // Both ends required: one date alone would render "1 July 2026 to —" inside the download link text.
  if (!summary.reportingPeriodStart || !summary.reportingPeriodEnd) {
    return quarterText;
  }

  return `${quarterText},\n${formatGovUkDate(summary.reportingPeriodStart)} to ${formatGovUkDate(summary.reportingPeriodEnd)}`;
};

const SubmissionListStatus = ({ status, testId }: { status: SubmissionStatus; testId: string }): React.ReactElement => {
  if (status === 'completed') {
    return (
      <span className="reports-orgs-row__status-text" data-testid={testId}>
        {displayStatusLabel.completed}
      </span>
    );
  }

  return (
    <span className={`reports-orgs-row__tag reports-orgs-row__tag--${status}`} data-testid={testId}>
      {displayStatusLabel[status]}
    </span>
  );
};

const NOT_FOUND_HEADING = 'Report not found';

const ReportOrganisations = (): React.ReactElement => {
  const navigate = useNavigate();
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const { data, isError, error, refetch } = useQuery(reportSubmissionsKey(collectionId), () => getReportSubmissions(collectionId), {
    enabled: !!collectionId,
    retry: reportQueryRetry,
  });
  const summary = data?.data;

  // Suppressed while a record is in hand — react-query v3 keeps cached data when a background refetch fails.
  const errorKind = reportLoadVerdict(!!summary, !!collectionId, isError, error);

  usePageTitle(reportPageTitle(errorKind, NOT_FOUND_HEADING, summary?.collectionName ?? 'Report'));

  const downloadMutation = useMutation(() => downloadWindowSubmissionsExport(collectionId));
  const searchFieldId = useId();
  const sortFieldId = useId();
  const statusFieldId = useId();
  const [searchTerm, setSearchTerm] = useState('');
  const [sortValue, setSortValue] = useState(sortOptions[0].value);
  const [statusFilter, setStatusFilter] = useState('');
  const [announcementRequest, setAnnouncementRequest] = useState(0);
  const [resultsAnnouncement, setResultsAnnouncement] = useState('');

  const organisations = useMemo(() => {
    const rows = summary?.organisations ?? [];
    const searched = searchTerm ? rows.filter((org) => org.organisationName.toLowerCase().includes(searchTerm.toLowerCase())) : rows;
    const filtered = statusFilter ? searched.filter((org) => org.status === statusFilter) : searched;
    const direction = sortValue.endsWith('-desc') ? 'desc' : 'asc';

    return [...filtered].sort((a, b) => {
      const comparison = a.organisationName.localeCompare(b.organisationName, undefined, { sensitivity: 'base' });
      return direction === 'desc' ? -comparison : comparison;
    });
  }, [summary?.organisations, searchTerm, sortValue, statusFilter]);

  const statusLabel = statusFilterOptions.find((option) => option.value === statusFilter)?.label;
  const sortLabel = sortOptions.find((option) => option.value === sortValue)?.label;
  const pendingResultsAnnouncement = [
    organisations.length === 0 ? 'No organisations found.' : `${organisations.length} ${organisations.length === 1 ? 'organisation' : 'organisations'} found.`,
    searchTerm ? `Search term: ${searchTerm}.` : '',
    statusLabel ? `Filter by status: ${statusLabel}.` : '',
    sortLabel ? `Sort by: ${sortLabel}.` : '',
  ]
    .filter(Boolean)
    .join(' ');

  useEffect(() => {
    if (announcementRequest === 0) {
      return;
    }

    setResultsAnnouncement('');
    const timer = window.setTimeout(() => setResultsAnnouncement(pendingResultsAnnouncement), 250);
    return () => window.clearTimeout(timer);
  }, [announcementRequest, pendingResultsAnnouncement]);

  const requestResultsAnnouncement = () => setAnnouncementRequest((request) => request + 1);

  // NotAuthorised carries its own <main>, so it replaces the layout rather than sitting inside it.
  if (errorKind === 'forbidden') {
    return <NotAuthorised goBack={navigate} testId="admin-reports-orgs-not-authorised" />;
  }

  if (errorKind) {
    return (
      <GeneralLayout breadcrumbs={reportBreadcrumbs()}>
        <ReportLoadFailure
          kind={errorKind}
          notFoundHeading={NOT_FOUND_HEADING}
          message={reportFailureMessage(errorKind, REPORT_NOT_FOUND_MESSAGE, error)}
          returnTo={ROUTES.admin.reports.list}
          returnLabel="Return to reports"
          onRetry={() => void refetch()}
          testId="admin-reports-orgs-load-failure"
        />
      </GeneralLayout>
    );
  }

  if (!summary) {
    return (
      <GeneralLayout breadcrumbs={reportBreadcrumbs()}>
        <PageLoading />
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout breadcrumbs={reportBreadcrumbs()}>
      <div className="reports-orgs-page" data-testid="admin-reports-orgs-page">
        <div className="reports-orgs-page__overview">
          <span className="reports-orgs-page__caption">Report</span>
          <H1 className="reports-orgs-page__title">{summary.collectionName}</H1>
          <Paragraph className="reports-orgs-page__intro">
            These are the submission details for this report. You can download the consolidated data. Select an organisation&rsquo;s name to view their individual data.
          </Paragraph>
        </div>

        <div className="reports-orgs-page__section-header">
          <H2 className="reports-orgs-page__subheading">Details</H2>
          <RouterLink
            to={`/admin/collections/${collectionId}`}
            className="govuk-button govuk-button--secondary reports-orgs-page__manage-button"
            data-testid="admin-reports-orgs-manage-button">
            Manage data collection
          </RouterLink>
        </div>

        <dl className="reports-orgs-page__summary" data-testid="admin-reports-orgs-details-list">
          <div className="reports-orgs-page__summary-row">
            <dt>Status</dt>
            <dd data-testid="admin-reports-orgs-details-status">{REPORT_COLLECTION_STATUS_LABEL[summary.collectionStatus]}</dd>
          </div>
          <div className="reports-orgs-page__summary-row">
            <dt>Start date</dt>
            <dd data-testid="admin-reports-orgs-details-start-date">{formatGovUkDate(summary.startDate)}</dd>
          </div>
          <div className="reports-orgs-page__summary-row">
            <dt>Deadline</dt>
            <dd data-testid="admin-reports-orgs-details-deadline">{formatGovUkDate(summary.deadline)}</dd>
          </div>
        </dl>

        <div className="reports-orgs-page__heading-band">
          <H2 className="reports-orgs-page__subheading">Files</H2>
        </div>

        <div className="reports-orgs-file" data-testid="admin-reports-orgs-files">
          <span className="reports-orgs-file__icon" aria-hidden="true">
            <SpreadsheetIcon />
          </span>
          <div className="reports-orgs-file__body">
            <button
              type="button"
              className="reports-orgs-file__title"
              onClick={() => downloadMutation.mutate()}
              disabled={downloadMutation.isLoading}
              /* The button is named by the report title alone, so associate the captions below it: the file format,
                 plus the incomplete-data warning when the window is still open — that is the more important thing to
                 hear BEFORE downloading. Block comment, not `//`: a line comment here silently comments out every
                 attribute after it if the tag is ever collapsed onto one line. */
              aria-describedby={summary.collectionStatus !== 'closed' ? 'admin-reports-orgs-files-meta admin-reports-orgs-files-warning' : 'admin-reports-orgs-files-meta'}
              data-testid="admin-reports-orgs-files-download">
              {consolidatedReportTitle(summary)}
            </button>
            {/* The consolidated window export is a ZIP of per-section CSVs (summary + activity/funding/feedback,
                aggregated across every org), mirroring the per-organisation report download. No size is shown: there is
                no window-level size on the API, and measuring one would mean running the audited ExportSubmissionsCommand
                on every page load. */}
            <p className="reports-orgs-file__meta" id="admin-reports-orgs-files-meta" data-testid="admin-reports-orgs-files-meta">
              ZIP
            </p>
            {summary.collectionStatus !== 'closed' && (
              <GovUKWarningText className="reports-orgs-file__warning" id="admin-reports-orgs-files-warning" data-testid="admin-reports-orgs-files-warning">
                This file may contain incomplete data because the submission is still open.
              </GovUKWarningText>
            )}
            {downloadMutation.isError && (
              // role="alert" so a failed download is announced — focus stays on the button and nothing else
              // signals the failure (WCAG 2.2 SC 4.1.3).
              <p className="govuk-error-message reports-orgs-file__error" role="alert" data-testid="admin-reports-orgs-files-error">
                <span className="govuk-visually-hidden">Error:</span> {downloadErrorMessage(downloadMutation.error)}
              </p>
            )}
          </div>
        </div>

        <div className="reports-orgs-page__heading-band">
          <H2 className="reports-orgs-page__subheading">Organisations</H2>
        </div>

        <div className="reports-orgs-filter" data-testid="admin-reports-orgs-list-filters">
          <div className="reports-orgs-filter__search">
            <label htmlFor={searchFieldId} className="govuk-label">
              Search by name
            </label>
            <div className="reports-orgs-filter__search-control">
              <input
                id={searchFieldId}
                className="govuk-input reports-orgs-filter__input"
                type="search"
                value={searchTerm}
                onChange={(event) => {
                  setSearchTerm(event.target.value);
                  requestResultsAnnouncement();
                }}
                data-testid="admin-reports-orgs-list-search"
              />
              <button
                type="button"
                className="reports-orgs-filter__search-button"
                aria-label="Search"
                data-testid="admin-reports-orgs-list-search-button"
                onClick={requestResultsAnnouncement}>
                <SearchIcon />
              </button>
            </div>
          </div>

          <div className="reports-orgs-filter__select">
            <label htmlFor={sortFieldId} className="govuk-label">
              Sort by
            </label>
            <select
              id={sortFieldId}
              className="govuk-select"
              value={sortValue}
              onChange={(event) => {
                setSortValue(event.target.value);
                requestResultsAnnouncement();
              }}
              data-testid="admin-reports-orgs-list-sort">
              {sortOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>

          <div className="reports-orgs-filter__select">
            <label htmlFor={statusFieldId} className="govuk-label">
              Filter by status
            </label>
            <select
              id={statusFieldId}
              className="govuk-select"
              value={statusFilter}
              onChange={(event) => {
                setStatusFilter(event.target.value);
                requestResultsAnnouncement();
              }}
              data-testid="admin-reports-orgs-list-filter">
              <option value="">Select status</option>
              {statusFilterOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="govuk-visually-hidden" role="status" aria-live="polite" aria-atomic="true" data-testid="admin-reports-orgs-list-results-status">
          {resultsAnnouncement}
        </div>

        <div className="reports-orgs-list" data-testid="admin-reports-orgs-list">
          {organisations.length === 0 ? (
            <p className="reports-orgs-list__empty" data-testid="admin-reports-orgs-list-empty">
              No organisations are assigned to this reporting window.
            </p>
          ) : (
            organisations.map((org) => {
              const line = submittedLine(org);
              return (
                <div
                  className={`reports-orgs-row${line ? ' reports-orgs-row--submitted' : ''}`}
                  key={org.organisationId}
                  data-testid={`admin-reports-orgs-list-row-${org.organisationId}`}>
                  <div className="reports-orgs-row__primary">
                    <RouterLink
                      to={`/admin/reports/${collectionId}/organisations/${org.organisationId}`}
                      className="reports-orgs-row__link"
                      data-testid={`admin-reports-orgs-list-row-${org.organisationId}-name`}>
                      {org.organisationName}
                    </RouterLink>
                    <SubmissionListStatus status={org.status} testId={`admin-reports-orgs-list-row-${org.organisationId}-status`} />
                  </div>
                  {line && <p className="reports-orgs-row__submitted">{line}</p>}
                </div>
              );
            })
          )}
        </div>
      </div>
    </GeneralLayout>
  );
};

export default ReportOrganisations;
