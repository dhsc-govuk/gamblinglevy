import React from 'react';
import { NotAuthorised, PageLoading } from '../../../../components/GovUKComponents';
import { useQuery, useMutation } from 'react-query';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { GeneralLayout } from '../../../../layouts';
import { H1, H2, Paragraph } from '../../../../components/GovUKComponents';
import { SpreadsheetIcon } from '../../../../components/Icons';
import usePageTitle, { pageTitleWithName } from '../../../../hooks/usePageTitle';
import { reportSubmissionDetailKey } from '../../../../helpers/queriesParams';
import { getReportSubmissionDetail, type OrgSubmissionDetailDto } from '../queries';
import { downloadOrgReport } from '../mutations';
import { formatDateTime, formatGovUkDate, reportingPeriodValue, SUBMISSION_STATUS_LABEL } from '../display';
import { reportBreadcrumbs } from '../navigation';
import { reportFailureMessage, reportLoadVerdict, reportPageTitle, reportQueryRetry, SUBMISSION_NOT_FOUND_MESSAGE } from '../messages';
import { downloadErrorMessage } from '../../../../helpers/downloadErrors';
import ReportLoadFailure from '../../../../components/Global/Reports/ReportLoadFailure';
import { composeTestId } from '../../../../helpers/testIds';
import { ROUTES } from '../../../../helpers/routes';
import './styles.css';

interface DetailRow {
  label: string;
  value: React.ReactNode;
  action?: { href: string; label: string };
  testId: string;
}

// The downloadable ZIP's display name. Dated by the REPORTING PERIOD, not the collection window — this matches the
// LA-side card (views/Reports/SubmissionDetail) and the "Reporting period" row inside the ZIP's own summary.csv.
// It previously used startDate/deadline, which are the window and only coincide with the period when the collection
// was published with "window same as reporting period" = Yes (BLUM-7079).
const orgReportTitle = (detail: OrgSubmissionDetailDto): string => {
  if (!detail.reportingPeriodStart || !detail.reportingPeriodEnd) {
    return `Quarterly report for ${detail.organisationName}`;
  }
  return `Quarterly report for ${detail.organisationName}, ${formatGovUkDate(detail.reportingPeriodStart)} to ${formatGovUkDate(detail.reportingPeriodEnd)}`;
};

const NOT_FOUND_HEADING = 'Submission not found';

const ReportSubmissionDetail = (): React.ReactElement => {
  const navigate = useNavigate();
  const { collectionId = '', organisationId = '' } = useParams<{ collectionId: string; organisationId: string }>();
  const hasRouteParams = !!collectionId && !!organisationId;
  const { data, isError, error, refetch } = useQuery(reportSubmissionDetailKey(collectionId, organisationId), () => getReportSubmissionDetail(collectionId, organisationId), {
    enabled: hasRouteParams,
    retry: reportQueryRetry,
  });
  const detail = data?.data;

  // Suppressed while a record is in hand: react-query v3 keeps cached data when a background refetch fails, and a
  // good record beats any failure page.
  const errorKind = reportLoadVerdict(!!detail, hasRouteParams, isError, error);

  usePageTitle(reportPageTitle(errorKind, NOT_FOUND_HEADING, pageTitleWithName('Submission', detail?.organisationName)));

  const downloadMutation = useMutation(() => downloadOrgReport(collectionId, organisationId));

  // NotAuthorised renders its own <main>, so it replaces the layout rather than sitting inside it — otherwise the
  // page would carry two main landmarks.
  if (errorKind === 'forbidden') {
    return <NotAuthorised goBack={navigate} testId="admin-reports-detail-not-authorised" />;
  }

  // A 500 used to land here too, under a "Submission not found" heading: the page told admins the organisation had
  // no submission when the server had actually failed (BLUM-7216).
  if (errorKind) {
    return (
      <GeneralLayout breadcrumbs={reportBreadcrumbs()}>
        <ReportLoadFailure
          kind={errorKind}
          notFoundHeading={NOT_FOUND_HEADING}
          message={reportFailureMessage(errorKind, SUBMISSION_NOT_FOUND_MESSAGE, error)}
          /* Without a collection id there is no summary to return to, so the link falls back to the reports list
             and says so — "Return to the report summary" pointing at /admin/reports/ would be a lie. Block comment,
             not `//`: a line comment here silently comments out every attribute after it if the tag is ever
             collapsed onto one line. */
          returnTo={collectionId ? ROUTES.admin.reports.collection(collectionId) : ROUTES.admin.reports.list}
          returnLabel={collectionId ? 'Return to the report summary' : 'Return to reports'}
          onRetry={() => void refetch()}
          testId="admin-reports-detail-load-failure"
        />
      </GeneralLayout>
    );
  }

  if (!detail) {
    return (
      <GeneralLayout breadcrumbs={reportBreadcrumbs()}>
        <PageLoading />
      </GeneralLayout>
    );
  }

  const isCompleted = !!detail.submittedAt;

  const detailRows: DetailRow[] = [
    { label: 'Status', value: SUBMISSION_STATUS_LABEL[detail.status], testId: 'admin-reports-detail-status' },
    // The period being reported on. Sits above the "Submitted by/on" rows because it describes the report itself
    // rather than the act of submitting, and so it also shows for organisations that have not submitted (BLUM-7079).
    {
      label: 'Reporting period',
      value: reportingPeriodValue(detail.quarter, detail.reportingPeriodStart, detail.reportingPeriodEnd),
      testId: 'admin-reports-detail-reporting-period',
    },
    // The submission deadline, which is what the "Overdue" status above is derived from (SubmissionStatusRules keys
    // on it alone). Without it an admin chasing a late return has no date on this page explaining the status.
    { label: 'Deadline', value: formatGovUkDate(detail.deadline ?? null), testId: 'admin-reports-detail-deadline' },
  ];
  if (isCompleted) {
    detailRows.push({
      label: 'Submitted by',
      value: detail.submittedByName ?? '—',
      action: detail.submittedByEmail ? { href: `mailto:${detail.submittedByEmail}`, label: detail.submittedByEmail } : undefined,
      testId: 'admin-reports-detail-submitted-by',
    });
    detailRows.push({ label: 'Submitted on', value: formatDateTime(detail.submittedAt), testId: 'admin-reports-detail-submitted-on' });
  }

  return (
    <GeneralLayout breadcrumbs={reportBreadcrumbs([{ label: detail.collectionName, link: `/admin/reports/${collectionId}` }])}>
      <div className="reports-detail-page" data-testid="admin-reports-detail">
        <div className="reports-detail-page__overview">
          <span className="reports-detail-page__caption">{detail.collectionName}</span>
          <H1 className="reports-detail-page__title">{detail.organisationName}</H1>
          <Paragraph className="reports-detail-page__intro">
            These are the submission details for this organisation. When available, you can download their report for further analysis.
          </Paragraph>
        </div>

        <div className="reports-detail-page__heading-band">
          <H2 className="reports-detail-page__subheading">Details</H2>
        </div>

        <dl className="reports-detail-page__summary" data-testid="admin-reports-detail-list">
          {detailRows.map((row) => (
            <div className="reports-detail-page__summary-row" key={row.label}>
              <dt>{row.label}</dt>
              <dd data-testid={row.testId}>{row.value}</dd>
              {row.action && (
                <dd className="reports-detail-page__summary-action">
                  <a className="govuk-link" href={row.action.href} data-testid={composeTestId(row.testId, 'action')}>
                    {row.action.label}
                  </a>
                </dd>
              )}
            </div>
          ))}
        </dl>

        <Paragraph>
          <RouterLink to={ROUTES.admin.organisations.detail(detail.organisationId)} className="govuk-link" data-testid="admin-reports-detail-view-organisation">
            View organisation
          </RouterLink>
        </Paragraph>

        <div className="reports-detail-page__heading-band">
          <H2 className="reports-detail-page__subheading">Files</H2>
        </div>

        <div className="reports-detail-page__file" data-testid="admin-reports-detail-files">
          <span className="reports-detail-page__file-icon" aria-hidden="true">
            <SpreadsheetIcon />
          </span>
          <div className="reports-detail-page__file-body">
            {isCompleted ? (
              <>
                <button
                  type="button"
                  className="reports-detail-page__file-title"
                  onClick={() => downloadMutation.mutate()}
                  disabled={downloadMutation.isLoading}
                  /* The button's accessible name is the report title alone, so associate the format caption — it is
                     the only thing telling a screen-reader user this control downloads a file rather than navigating.
                     Block comment, not `//`: a line comment here silently comments out every attribute that follows
                     it if the tag is ever collapsed onto one line. */
                  aria-describedby="admin-reports-detail-files-meta"
                  data-testid="admin-reports-detail-files-download">
                  {orgReportTitle(detail)}
                </button>
                {/* Format only, no byte size — a known shortfall against the GDS attachment pattern, which expects
                    format AND size so a user on a slow or metered connection can decide before committing. The size
                    was removed because the only way to know it was to build the organisation's whole report ZIP on
                    every view of this page, and again on download; measuring it also gave a read a way to fail,
                    which left a download button that then 500d. Restoring it properly means persisting the byte
                    length at submit time alongside ReportSnapshotJson (and versioning it, since the CSV/ZIP writers
                    can change under a frozen snapshot). The LA card and the window export show "ZIP" alone too —
                    that is the same gap, not a precedent. */}
                <p className="reports-detail-page__file-meta" id="admin-reports-detail-files-meta" data-testid="admin-reports-detail-files-meta">
                  ZIP
                </p>
                {downloadMutation.isError && (
                  // role="alert" so a failed download is announced: focus stays on the button, and without a live
                  // region a screen-reader user is left waiting for a file that never arrives (WCAG 2.2 SC 4.1.3).
                  <p className="govuk-error-message reports-detail-page__file-error" role="alert" data-testid="admin-reports-detail-files-error">
                    <span className="govuk-visually-hidden">Error:</span> {downloadErrorMessage(downloadMutation.error)}
                  </p>
                )}
              </>
            ) : (
              <>
                <p className="reports-detail-page__file-title reports-detail-page__file-title--placeholder">{orgReportTitle(detail)}</p>
                <p className="reports-detail-page__file-empty">The file will be available to download once the organisation has submitted their data.</p>
              </>
            )}
          </div>
        </div>

        <Paragraph>
          <RouterLink to={`/admin/reports/${collectionId}`} className="govuk-link" data-testid="admin-reports-detail-return">
            Return to the report summary
          </RouterLink>
        </Paragraph>
      </div>
    </GeneralLayout>
  );
};

export default ReportSubmissionDetail;
