import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { useQuery, useMutation } from '@tanstack/react-query';
import { Link as RouterLink, useParams } from 'react-router-dom';
import { GeneralLayout } from '../../../layouts';
import { SummaryList, Paragraph } from '../../../components/GovUKComponents';
import FileDownloadCard from '../../../components/Global/Reports/FileDownloadCard';
import ReportLoadFailure from '../../../components/Global/Reports/ReportLoadFailure';
import SubmittedByValue from '../../../components/Global/Reports/SubmittedBy';
import { fileMetadata } from '../../../helpers/fileSize';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { mySubmissionDetailKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { SUBMISSION_STATUS_LABEL } from '../../Admin/Reports/display';
// The retry rule is shared with the DHSC Reports screens rather than restated here: a 404 is retry-proof, a 429 is
// retry-hostile, and a 403 must keep its retries so the axios interceptor's post-verify guard can redirect. Those
// reasons do not survive being paraphrased into a fourth copy (the useOrganisationUser precedent).
import { reportQueryRetry } from '../../Admin/Reports/messages';
import { getMySubmissionDetail } from '../queries';
import { downloadReportExport } from '../mutations';
import { collectionDisplayName, formatGovUkDate, formatSubmittedDateTime } from '../display';
import MaturitySubmittedDetail from '../../MaturityAssessment/SubmittedDetail';
import { SUBMISSION_DETAIL_NOT_FOUND_HEADING, submissionDetailFailureMessage, submissionDetailPageTitle, submissionDetailVerdict } from '../messages';
import { downloadErrorMessage } from '../../../helpers/downloadErrors';
import { submissionDetailBreadcrumbs } from '../navigation';

// LA submission detail (BLUM-6098). The read-only record of one finalised report — status, who/when submitted, and
// the downloadable ZIP. No DHSC verification controls. Org-scoped server-side (404 for another org's collection).
// Layout per the design (Figma 420-80777): 36px H1 under a "Report" caption, plain "Details"/"Files" headings (no
// summary card), and everything in a two-thirds column. The design omits the beta phase banner, but that is now
// global chrome on every page per BLUM-7060 AC1 (see BLUM-7102).
const SubmissionDetail = (): React.ReactElement => {
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const { userRole } = useAuthProvider();

  const { data, isError, error, refetch } = useQuery({
    queryKey: mySubmissionDetailKey(collectionId),
    queryFn: () => getMySubmissionDetail(collectionId),
    enabled: !!collectionId,
    retry: reportQueryRetry,
  });
  const detail = data?.data;
  const title = detail ? collectionDisplayName({ ...detail, type: detail.type ?? null, financialYear: detail.financialYear ?? null }) : 'Report';

  // Suppressed while a record is in hand — see submissionDetailVerdict for the precedence and for why a refusal and
  // a missing record are the same verdict on this organisation-scoped surface.
  const errorKind = submissionDetailVerdict(!!detail, !!collectionId, isError, error);

  usePageTitle(submissionDetailPageTitle(errorKind, detail ? title : 'Report'));

  const downloadMutation = useMutation({ mutationFn: () => downloadReportExport(collectionId) });

  // Until BLUM-6101 every failure landed on the not-found page below, so an outage told an LA user their report did
  // not exist — the BLUM-7216 defect, fixed on the DHSC screens at the time and not on this one.
  if (errorKind) {
    return (
      // The full trail, not the landing one: all three crumbs are static, so none of them depends on the record
      // that failed to load, and a page whose whole point is "you cannot see this one" should offer more ways back
      // rather than fewer. Matches the DHSC screen on the same state.
      <GeneralLayout breadcrumbs={submissionDetailBreadcrumbs(userRole)}>
        <ReportLoadFailure
          kind={errorKind}
          notFoundHeading={SUBMISSION_DETAIL_NOT_FOUND_HEADING}
          message={submissionDetailFailureMessage(errorKind, error)}
          returnTo={ROUTES.reports.submitted}
          returnLabel="Return to your reports"
          onRetry={() => void refetch()}
          testId="la-reports-detail-load-failure"
        />
      </GeneralLayout>
    );
  }

  if (!detail) {
    return (
      <GeneralLayout breadcrumbs={submissionDetailBreadcrumbs(userRole)}>
        <PageLoading />
      </GeneralLayout>
    );
  }

  // A maturity window's record is the maturity read-only view (BLUM-7859): same history list, same route, its own
  // body — the sections are read from the frozen snapshot rather than downloaded per section.
  if (detail.type === 'maturity_assessment') {
    return <MaturitySubmittedDetail detail={detail} />;
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

  const detailItems: { label: string; value: React.ReactNode; testId?: string }[] = [
    // Plain text (no tag) per the design, using the shared status wording ("Completed").
    { label: 'Status', value: SUBMISSION_STATUS_LABEL[detail.status], testId: 'la-reports-detail-status' },
  ];
  if (isCompleted) {
    const submittedByTestId = 'la-reports-detail-submitted-by';
    detailItems.push({
      label: 'Submitted by',
      // The email follows the name in the same cell, not the action column (BLUM-9018): no row has an action, so the
      // value column takes the full 70% and the name and an ordinary address fit on one line.
      value: <SubmittedByValue name={detail.submittedByName} email={detail.submittedByEmail} testId={submittedByTestId} />,
      testId: submittedByTestId,
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
                metadata={fileMetadata('ZIP', detail.exportSizeBytes)}
                onDownload={() => downloadMutation.mutate()}
                isLoading={downloadMutation.isPending}
                errorMessage={downloadMutation.isError ? downloadErrorMessage(downloadMutation.error) : null}
                testId="la-reports-detail-files"
              />
            ) : (
              <Paragraph data-testid="la-reports-detail-files">{noFilesMessage}</Paragraph>
            )}

            <Paragraph>
              <RouterLink to={ROUTES.reports.submitted} className="govuk-link" data-testid="la-reports-detail-return">
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
