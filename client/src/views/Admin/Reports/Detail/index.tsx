import React from 'react';
import { NotAuthorised, PageLoading } from '../../../../components/GovUKComponents';
import { useQuery, useMutation } from '@tanstack/react-query';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { GeneralLayout } from '../../../../layouts';
import { GovUKTaskList, H1, H2, Paragraph, type TaskListItem } from '../../../../components/GovUKComponents';
import ReadOnlySectionCards from '../../../../components/Global/MaturityAssessment/ReadOnlySectionCards';
import { progressTaskStatus } from '../../../Reports/display';
import { maturityAssessmentName } from '../../../MaturityAssessment/names';
import { financialYearEndYear } from '../../../../helpers/financialYear';
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
import SubmittedByValue from '../../../../components/Global/Reports/SubmittedBy';
import { fileMetadata } from '../../../../helpers/fileSize';
import { composeTestId, slugifyTestId } from '../../../../helpers/testIds';
import { ROUTES } from '../../../../helpers/routes';
import './styles.css';

interface DetailRow {
  label: string;
  value: React.ReactNode;
  testId: string;
}

// The downloadable ZIP's display name. Dated by the REPORTING PERIOD, not the collection window — this matches the
// LA-side card (views/Reports/SubmissionDetail) and the "Reporting period" row inside the ZIP's own summary.csv.
// It previously used startDate/deadline, which are the window and only coincide with the period when the collection
// was published with "window same as reporting period" = Yes (BLUM-7079).
const orgReportTitle = (detail: OrgSubmissionDetailDto): string => {
  if (detail.collectionType === 'maturity_assessment') {
    return detail.financialYear ? `Maturity assessment for ${detail.organisationName}, ${detail.financialYear}` : `Maturity assessment for ${detail.organisationName}`;
  }
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
  const { data, isError, error, refetch } = useQuery({
    queryKey: reportSubmissionDetailKey(collectionId, organisationId),
    queryFn: () => getReportSubmissionDetail(collectionId, organisationId),
    enabled: hasRouteParams,
    retry: reportQueryRetry,
  });
  const detail = data?.data;

  // Suppressed while a record is in hand: react-query keeps cached data when a background refetch fails, and a
  // good record beats any failure page.
  const errorKind = reportLoadVerdict(!!detail, hasRouteParams, isError, error);

  usePageTitle(reportPageTitle(errorKind, NOT_FOUND_HEADING, pageTitleWithName('Submission', detail?.organisationName)));

  const downloadMutation = useMutation({ mutationFn: () => downloadOrgReport(collectionId, organisationId) });

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
  const isMaturity = detail.collectionType === 'maturity_assessment';

  const detailRows: DetailRow[] = [
    { label: 'Status', value: SUBMISSION_STATUS_LABEL[detail.status], testId: 'admin-reports-detail-status' },
    // A maturity window covers a whole financial year, which is how the LA screens name it (BLUM-7859).
    ...(isMaturity ? [{ label: 'Financial year', value: detail.financialYear ?? '—', testId: 'admin-reports-detail-financial-year' }] : []),
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
    const submittedByTestId = 'admin-reports-detail-submitted-by';
    detailRows.push({
      label: 'Submitted by',
      // The email follows the name in the same cell, as on the LA's record (BLUM-9018).
      value: <SubmittedByValue name={detail.submittedByName} email={detail.submittedByEmail} testId={submittedByTestId} />,
      testId: submittedByTestId,
    });
    detailRows.push({ label: 'Submitted on', value: formatDateTime(detail.submittedAt), testId: 'admin-reports-detail-submitted-on' });
    if (isMaturity && detail.referenceNumber) {
      detailRows.push({ label: 'Reference number', value: detail.referenceNumber, testId: 'admin-reports-detail-reference' });
    }
  }

  // A maturity assessment is read here rather than only downloaded (BLUM-7859 AC2): section statuses while it is
  // in progress, and every frozen answer once submitted.
  const maturitySectionItems: TaskListItem[] = (detail.maturitySections ?? []).map((section) => ({
    id: section.code,
    title: section.title,
    status: progressTaskStatus(section.status),
    testId: composeTestId('admin-reports-detail-maturity-sections', 'item', slugifyTestId(section.code)),
  }));

  return (
    <GeneralLayout breadcrumbs={reportBreadcrumbs([{ label: detail.collectionName, link: `/admin/reports/${collectionId}` }])}>
      <div className="reports-detail-page" data-testid="admin-reports-detail">
        <div className="reports-detail-page__overview">
          <span className="reports-detail-page__caption">{isMaturity ? maturityAssessmentName(financialYearEndYear(detail.financialYear ?? '')) : detail.collectionName}</span>
          <H1 className="reports-detail-page__title">{detail.organisationName}</H1>
          <Paragraph className="reports-detail-page__intro">
            {isMaturity
              ? 'These are the maturity assessment details for this organisation. Once submitted, you can read their answers below or download them for further analysis.'
              : 'These are the submission details for this organisation. When available, you can download their report for further analysis.'}
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
            </div>
          ))}
        </dl>

        <Paragraph>
          <RouterLink to={ROUTES.admin.organisations.detail(detail.organisationId)} className="govuk-link" data-testid="admin-reports-detail-view-organisation">
            View organisation
          </RouterLink>
        </Paragraph>

        {isMaturity && !isCompleted && maturitySectionItems.length > 0 && (
          <>
            <div className="reports-detail-page__heading-band">
              <H2 className="reports-detail-page__subheading">Sections</H2>
            </div>
            <GovUKTaskList testId="admin-reports-detail-maturity-sections" idPrefix="admin-reports-detail-maturity-sections" items={maturitySectionItems} />
          </>
        )}

        {isMaturity && isCompleted && (detail.maturitySnapshot ?? []).length > 0 && (
          <>
            <div className="reports-detail-page__heading-band">
              <H2 className="reports-detail-page__subheading">Answers</H2>
            </div>
            <div data-testid="admin-reports-detail-maturity-answers">
              {(detail.maturitySnapshot ?? []).map((section) => {
                const sectionId = composeTestId('admin-reports-detail-maturity', slugifyTestId(section.code)) ?? section.code;
                return (
                  <section key={section.code} aria-labelledby={sectionId}>
                    <h3 className="govuk-heading-m" id={sectionId}>
                      {section.title}
                    </h3>
                    <ReadOnlySectionCards section={section} headingLevel={4} testId={sectionId} />
                  </section>
                );
              })}
            </div>
          </>
        )}

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
                  disabled={downloadMutation.isPending}
                  /* The button's accessible name is the report title alone, so associate the format caption — it is
                     the only thing telling a screen-reader user this control downloads a file rather than navigating.
                     Block comment, not `//`: a line comment here silently comments out every attribute that follows
                     it if the tag is ever collapsed onto one line. */
                  aria-describedby="admin-reports-detail-files-meta"
                  data-testid="admin-reports-detail-files-download">
                  {orgReportTitle(detail)}
                </button>
                {/* Format and size together, the GDS attachment pattern ("ZIP, 389 KB"), so a user on a slow or
                    metered connection can decide before committing (BLUM-9018). The server measures this one
                    organisation's ZIP through the download's own call, and falls back to null — the format alone —
                    rather than failing the page when it cannot (ReportExportSizeService). The window-wide export stays
                    format-only: measuring it would mean building every organisation's report on each view. The LA's
                    post-submit confirmation pages (Reports/Submitted, MaturityAssessment/Submitted) still show the
                    format alone — the same gap, left for a follow-up. */}
                <p className="reports-detail-page__file-meta" id="admin-reports-detail-files-meta" data-testid="admin-reports-detail-files-meta">
                  {fileMetadata('ZIP', detail.exportSizeBytes)}
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
