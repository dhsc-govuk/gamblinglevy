import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { useQuery, useMutation } from 'react-query';
import { Link as RouterLink, useParams } from 'react-router-dom';
import { GeneralLayout } from '../../../layouts';
import { SummaryList, Paragraph } from '../../../components/GovUKComponents';
import FileDownloadCard from '../../../components/Global/Reports/FileDownloadCard';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { mySubmissionDetailKey } from '../../../helpers/queriesParams';
import { SUBMISSION_STATUS_LABEL } from '../../Admin/Reports/display';
import { getMySubmissionDetail } from '../queries';
import { downloadReportExport } from '../mutations';
import { formatGovUkDate, formatSubmittedDateTime, reportName } from '../display';
import { mapReportError } from '../messages';
import { downloadErrorMessage } from '../../../helpers/downloadErrors';
import { reportsLandingBreadcrumbs, submissionDetailBreadcrumbs } from '../navigation';

// LA submission detail (BLUM-6098). The read-only record of one finalised report — status, who/when submitted, and
// the downloadable ZIP. No DHSC verification controls. Org-scoped server-side (404 for another org's collection).
// Layout per the design (Figma 420-80777): 36px H1 under a "Report" caption, plain "Details"/"Files" headings (no
// summary card), and everything in a two-thirds column. The design omits the beta phase banner, but that is now
// global chrome on every page per BLUM-7060 AC1 (see BLUM-7102).
const SubmissionDetail = (): React.ReactElement => {
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const { userRole } = useAuthProvider();

  const { data, isLoading, error } = useQuery(mySubmissionDetailKey(collectionId), () => getMySubmissionDetail(collectionId), { enabled: !!collectionId });
  const detail = data?.data;
  const title = detail ? reportName(detail.quarter, detail.reportingPeriodStart, detail.reportingPeriodEnd) : 'Report';
  usePageTitle(detail ? title : 'Report');

  const downloadMutation = useMutation(() => downloadReportExport(collectionId));

  if (isLoading) {
    return (
      <GeneralLayout breadcrumbs={submissionDetailBreadcrumbs(userRole)}>
        <PageLoading />
      </GeneralLayout>
    );
  }

  if (!detail) {
    return (
      <GeneralLayout breadcrumbs={reportsLandingBreadcrumbs(userRole)}>
        <h1 className="govuk-heading-l">Report not found</h1>
        <Paragraph>{mapReportError(error).message}</Paragraph>
        <RouterLink to="/reports/submitted" className="govuk-link" data-testid="la-reports-detail-return">
          Return to your reports
        </RouterLink>
      </GeneralLayout>
    );
  }

  // The "Submitted by/on" rows and the report download are tied to a *finalised* report (submittedAt set), not merely
  // a started one — a started-but-unsubmitted submission has no submitter, no submitted date, and no exportable ZIP
  // (the export endpoint 404s until submit). Mirrors the DHSC detail view's isCompleted gate.
  const isCompleted = !!detail.submittedAt;

  // An overdue period is past its deadline and its collection is closed, so there is nothing still to submit —
  // promising a future download would be wrong (BLUM-7077).
  const noFilesMessage =
    detail.status === 'overdue'
      ? 'You did not submit this report before the deadline, so there is nothing to download.'
      : 'The report will be available to download once you have submitted it.';

  const detailItems: { label: string; value: React.ReactNode; actionHref?: string; actionLabel?: string; testId?: string }[] = [
    // Plain text (no tag) per the design, using the shared status wording ("Completed").
    { label: 'Status', value: SUBMISSION_STATUS_LABEL[detail.status], testId: 'la-reports-detail-status' },
  ];
  if (isCompleted) {
    detailItems.push({
      label: 'Submitted by',
      value: detail.submittedByName ?? '—',
      // The submitter's email renders in the row's action column per the design.
      ...(detail.submittedByEmail ? { actionHref: `mailto:${detail.submittedByEmail}`, actionLabel: detail.submittedByEmail } : {}),
      testId: 'la-reports-detail-submitted-by',
    });
    detailItems.push({ label: 'Submitted on', value: formatSubmittedDateTime(detail.submittedAt), testId: 'la-reports-detail-submitted-on' });
  }

  return (
    <GeneralLayout breadcrumbs={submissionDetailBreadcrumbs(userRole)}>
      <div data-testid="la-reports-detail">
        <div className="govuk-grid-row">
          <div className="govuk-grid-column-two-thirds">
            <span className="govuk-caption-l">Report</span>
            <h1 className="govuk-heading-l">{title}</h1>

            <h2 className="govuk-heading-m">Details</h2>
            <SummaryList testId="la-reports-detail-list" items={detailItems} />

            <h2 className="govuk-heading-m">Files</h2>
            {isCompleted ? (
              <FileDownloadCard
                title={`Quarterly report for ${detail.localAuthorityName}, ${formatGovUkDate(detail.reportingPeriodStart)} to ${formatGovUkDate(detail.reportingPeriodEnd)}`}
                metadata="ZIP"
                onDownload={() => downloadMutation.mutate()}
                isLoading={downloadMutation.isLoading}
                errorMessage={downloadMutation.isError ? downloadErrorMessage(downloadMutation.error) : null}
                testId="la-reports-detail-files"
              />
            ) : (
              <Paragraph data-testid="la-reports-detail-files">{noFilesMessage}</Paragraph>
            )}

            <Paragraph>
              <RouterLink to="/reports/submitted" className="govuk-link" data-testid="la-reports-detail-return">
                Return to your reports
              </RouterLink>
            </Paragraph>
          </div>
        </div>
      </div>
    </GeneralLayout>
  );
};

export default SubmissionDetail;
