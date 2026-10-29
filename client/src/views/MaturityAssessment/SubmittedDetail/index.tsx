import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { PageLoading, Paragraph, SummaryList } from '../../../components/GovUKComponents';
import FileDownloadCard from '../../../components/Global/Reports/FileDownloadCard';
import SubmittedByValue from '../../../components/Global/Reports/SubmittedBy';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import { maturitySubmittedKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { financialYearEndYear } from '../../../helpers/financialYear';
import { downloadErrorMessage } from '../../../helpers/downloadErrors';
import { fileMetadata } from '../../../helpers/fileSize';
import { SUBMISSION_STATUS_LABEL } from '../../Admin/Reports/display';
import { reportQueryRetry } from '../../Admin/Reports/messages';
import type { MySubmissionDetailDto } from '../../Reports/queries';
import { formatGovUkDate, formatSubmittedDateTime } from '../../Reports/display';
import { submissionDetailBreadcrumbs } from '../../Reports/navigation';
import { getSubmittedMaturityAssessment } from '../queries';
import { downloadMaturityExport } from '../mutations';
import { maturityAssessmentName } from '../display';

interface SubmittedDetailProps {
  detail: MySubmissionDetailDto;
}

// The LA's read-only record of one maturity assessment (BLUM-7859 AC1, Figma 2744-181527): status, who and when,
// the reference, a plain list of sections to read through and the downloadable ZIP. Rendered by
// views/Reports/SubmissionDetail when the collection is a maturity window, so the history list needs no route.
const SubmittedDetail = ({ detail }: SubmittedDetailProps): React.ReactElement => {
  const { userRole } = useAuthProvider();
  const endYear = financialYearEndYear(detail.financialYear ?? '');
  const title = maturityAssessmentName(endYear);
  usePageTitle(title);

  const isCompleted = !!detail.submittedAt;
  const collectionId = detail.collectionId;

  // The frozen snapshot: only once submitted (404 before), and only for the section links — the header rows above
  // come from the shared history detail.
  const submittedQuery = useQuery({
    queryKey: maturitySubmittedKey(collectionId),
    queryFn: () => getSubmittedMaturityAssessment(collectionId),
    enabled: isCompleted,
    retry: reportQueryRetry,
  });
  const submitted = submittedQuery.data?.data;

  const downloadMutation = useMutation({ mutationFn: () => downloadMaturityExport(collectionId) });

  const detailItems: { label: string; value: React.ReactNode; testId?: string }[] = [
    { label: 'Status', value: SUBMISSION_STATUS_LABEL[detail.status], testId: 'la-reports-detail-status' },
  ];
  if (isCompleted) {
    const submittedByTestId = 'la-reports-detail-submitted-by';
    detailItems.push({
      label: 'Submitted by',
      // In the value cell after the name, as on the quarterly record (BLUM-9018).
      value: <SubmittedByValue name={detail.submittedByName} email={detail.submittedByEmail} testId={submittedByTestId} />,
      testId: submittedByTestId,
    });
    detailItems.push({ label: 'Submitted on', value: formatSubmittedDateTime(detail.submittedAt), testId: 'la-reports-detail-submitted-on' });
    if (detail.referenceNumber) {
      detailItems.push({ label: 'Reference number', value: detail.referenceNumber, testId: 'la-reports-detail-reference' });
    }
  }

  const sections = submitted?.sections ?? [];

  // formatGovUkDate renders a null as an em dash, so fall back to the financial year rather than ", — to —" when a
  // collection arrives without a reporting period (both dates are nullable on this DTO).
  const downloadPeriod =
    detail.reportingPeriodStart && detail.reportingPeriodEnd
      ? `${formatGovUkDate(detail.reportingPeriodStart)} to ${formatGovUkDate(detail.reportingPeriodEnd)}`
      : detail.financialYear ?? '';

  const noFilesMessage =
    detail.status === 'overdue'
      ? 'You did not submit this maturity assessment before the deadline, so there is nothing to download.'
      : 'The maturity assessment will be available to download once you have submitted it.';

  return (
    <GeneralLayout breadcrumbs={submissionDetailBreadcrumbs(userRole)}>
      <div data-testid="la-reports-detail">
        <div className="govuk-grid-row">
          <div className="govuk-grid-column-two-thirds">
            <span className="govuk-caption-l">Maturity assessment</span>
            <h1 className="govuk-heading-l">{title}</h1>

            <h2 className="govuk-heading-m">Details</h2>
            <SummaryList testId="la-reports-detail-list" items={detailItems} />

            {isCompleted && (
              <>
                <h2 className="govuk-heading-m">Sections</h2>
                {submittedQuery.isLoading ? (
                  <PageLoading />
                ) : sections.length > 0 ? (
                  <>
                    <Paragraph data-testid="la-reports-detail-sections-intro">
                      You can view the answers your organisation submitted for each section, but you cannot make changes.
                    </Paragraph>
                    {/* A raw <ul>, as GuidanceContents does: UnorderedList from GovUKComponents hardcodes
                        govuk-list--bullet, and the design (Figma 2744-181527) is an unbulleted govuk-list. */}
                    <ul className="govuk-list govuk-list--spaced" data-testid="la-reports-detail-sections">
                      {sections.map((section) => {
                        const itemTestId = composeTestId('la-reports-detail-sections', 'item', slugifyTestId(section.code));
                        return (
                          <li key={section.code} data-testid={itemTestId}>
                            <RouterLink
                              to={ROUTES.reports.submittedMaturitySection(collectionId, section.code)}
                              className="govuk-link"
                              data-testid={composeTestId(itemTestId, 'link')}>
                              {section.title}
                            </RouterLink>
                          </li>
                        );
                      })}
                    </ul>
                  </>
                ) : (
                  <Paragraph data-testid="la-reports-detail-sections-empty">The submitted answers are not available to view.</Paragraph>
                )}
              </>
            )}

            <h2 className="govuk-heading-m">Files</h2>
            {isCompleted ? (
              <FileDownloadCard
                title={`Maturity assessment for ${detail.localAuthorityName}${downloadPeriod ? `, ${downloadPeriod}` : ''}`}
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

export default SubmittedDetail;
