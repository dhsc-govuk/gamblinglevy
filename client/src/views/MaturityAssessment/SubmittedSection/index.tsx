import React from 'react';
import { Link as RouterLink, Navigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { GovUKPagination, H1, PageLoading, Paragraph } from '../../../components/GovUKComponents';
import ReadOnlySectionCards from '../../../components/Global/MaturityAssessment/ReadOnlySectionCards';
import ReportLoadFailure from '../../../components/Global/Reports/ReportLoadFailure';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle, { pageTitleWithName } from '../../../hooks/usePageTitle';
import { maturitySubmittedKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { reportQueryRetry } from '../../Admin/Reports/messages';
import { SUBMISSION_DETAIL_NOT_FOUND_HEADING, submissionDetailFailureMessage, submissionDetailPageTitle, submissionDetailVerdict } from '../../Reports/messages';
import { submissionDetailBreadcrumbs } from '../../Reports/navigation';
import { getSubmittedMaturityAssessment } from '../queries';
import { maturityAssessmentName } from '../display';

// One section of a submitted maturity assessment, read-only (BLUM-7859 AC1, Figma 2744-181590): a Back link, the
// frozen answers as summary cards, Previous/Next between sections, and a way back to the assessment's record.
// The loading state shows the same Back link as the loaded page, so the chrome does not change shape on the
// success path. The failure state keeps breadcrumbs on purpose — the record the Back link points at may not
// exist either, so a trail up to "Your reports" is the more useful escape.
const SubmittedSection = (): React.ReactElement => {
  const { collectionId = '', sectionCode = '' } = useParams<{ collectionId: string; sectionCode: string }>();
  const { userRole } = useAuthProvider();

  const { data, isError, error, refetch } = useQuery({
    queryKey: maturitySubmittedKey(collectionId),
    queryFn: () => getSubmittedMaturityAssessment(collectionId),
    enabled: !!collectionId,
    retry: reportQueryRetry,
  });
  const submitted = data?.data;
  const index = submitted ? submitted.sections.findIndex((section) => section.code === sectionCode) : -1;
  const section = index >= 0 ? submitted?.sections[index] : undefined;

  const errorKind = submissionDetailVerdict(!!submitted, !!collectionId, isError, error);
  usePageTitle(submissionDetailPageTitle(errorKind, section ? pageTitleWithName(section.title, maturityAssessmentName(submitted?.financialYearEndYear)) : 'Maturity assessment'));

  const detailPath = ROUTES.reports.submittedDetail(collectionId);
  const crumbs = [...submissionDetailBreadcrumbs(userRole), ...(submitted ? [{ label: maturityAssessmentName(submitted.financialYearEndYear), link: detailPath }] : [])];

  if (errorKind) {
    return (
      <GeneralLayout breadcrumbs={crumbs}>
        <ReportLoadFailure
          kind={errorKind}
          notFoundHeading={SUBMISSION_DETAIL_NOT_FOUND_HEADING}
          message={submissionDetailFailureMessage(errorKind, error)}
          returnTo={ROUTES.reports.submitted}
          returnLabel="Return to your reports"
          onRetry={() => void refetch()}
          testId="la-maturity-section-load-failure"
        />
      </GeneralLayout>
    );
  }

  if (!submitted) {
    return (
      <GeneralLayout backLink={{ href: detailPath, testId: 'la-maturity-section-back' }}>
        <PageLoading />
      </GeneralLayout>
    );
  }

  // An unknown section code: back to the record rather than a blank page.
  if (!section) {
    return <Navigate to={detailPath} replace />;
  }

  const previous = index > 0 ? submitted.sections[index - 1] : undefined;
  const next = index < submitted.sections.length - 1 ? submitted.sections[index + 1] : undefined;

  return (
    <GeneralLayout backLink={{ href: detailPath, testId: 'la-maturity-section-back' }}>
      <div data-testid="la-maturity-section">
        <span className="govuk-caption-l">{maturityAssessmentName(submitted.financialYearEndYear)}</span>
        <H1>{section.title}</H1>

        <ReadOnlySectionCards section={section} testId="la-maturity-section-cards" />

        <GovUKPagination
          ariaLabel="Sections"
          previous={previous ? { label: previous.title, href: ROUTES.reports.submittedMaturitySection(collectionId, previous.code) } : undefined}
          next={next ? { label: next.title, href: ROUTES.reports.submittedMaturitySection(collectionId, next.code) } : undefined}
          testId="la-maturity-section-pagination"
        />

        <Paragraph>
          <RouterLink to={detailPath} className="govuk-link" data-testid="la-maturity-section-return">
            Return to your maturity assessment
          </RouterLink>
        </Paragraph>
      </div>
    </GeneralLayout>
  );
};

export default SubmittedSection;
