import React, { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { GovUKButton, GovUKPanel, PageLoading, Paragraph } from '../../../components/GovUKComponents';
import FileDownloadCard from '../../../components/Global/Reports/FileDownloadCard';
import { homeLinkForRole, useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { ROUTES } from '../../../helpers/routes';
import { downloadErrorMessage } from '../../../helpers/downloadErrors';
import { formatGovUkDate } from '../../Reports/display';
import { downloadMaturityExport } from '../mutations';
import { useMaturityContext } from '../context';
import './styles.css';

interface SubmittedState {
  referenceNumber?: string;
}

// "Maturity assessment submitted" (BLUM-7858 AC4, Figma 2744-182370): the reference number, what happens next and a
// download of the finalised assessment. The confirmation email is best-effort server-side, so the line below is the
// expected-path copy rather than a delivery guarantee.
const Submitted = (): React.ReactElement => {
  usePageTitle('Maturity assessment submitted');
  const navigate = useNavigate();
  const location = useLocation();
  const { user, userRole } = useAuthProvider();
  const [downloadError, setDownloadError] = useState<string | null>(null);

  const { collectionId, context, isLoading, isError } = useMaturityContext();

  const downloadMutation = useMutation({
    mutationFn: () => downloadMaturityExport(collectionId),
    onError: (error) => setDownloadError(downloadErrorMessage(error)),
    onSuccess: () => setDownloadError(null),
  });

  const stateReference = (location.state as SubmittedState | null)?.referenceNumber ?? null;
  const referenceNumber = stateReference ?? context?.referenceNumber ?? null;

  if (isError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  // Only reachable for a submitted assessment.
  if (!isLoading && !stateReference && (!context || !context.submittedAt)) {
    return <Navigate to={ROUTES.reports.maturity(collectionId)} replace />;
  }

  return (
    <GeneralLayout>
      <div className="maturity-submitted" data-testid="la-maturity-submitted">
        {isLoading || !context ? (
          <PageLoading />
        ) : (
          <>
            <GovUKPanel className="maturity-submitted__panel" title="Maturity assessment submitted" data-testid="la-maturity-submitted-panel">
              Your reference number
              <br />
              <strong data-testid="la-maturity-submitted-reference">{referenceNumber}</strong>
            </GovUKPanel>

            <section className="maturity-submitted__section" aria-labelledby="la-maturity-submitted-next-heading">
              <h2 className="govuk-heading-m" id="la-maturity-submitted-next-heading">
                What happens next
              </h2>
              <Paragraph>A confirmation email has been sent to {user?.email ?? 'your registered email address'}.</Paragraph>
              <Paragraph>The Department of Health and Social Care (DHSC) will review your maturity assessment. They&rsquo;ll contact you if they have any questions.</Paragraph>
              <Paragraph>You&rsquo;ll receive an email notification when your next maturity assessment is due.</Paragraph>
            </section>

            <section className="maturity-submitted__section maturity-submitted__download" aria-labelledby="la-maturity-submitted-download-heading">
              <h2 className="govuk-heading-m" id="la-maturity-submitted-download-heading">
                Download your assessment
              </h2>
              <Paragraph>You can download a copy of your submitted maturity assessment for your records. You can also view it under &lsquo;View your reports&rsquo;.</Paragraph>

              <FileDownloadCard
                title={`Maturity assessment for ${context.localAuthorityName}, ${formatGovUkDate(context.reportingPeriodStart)} to ${formatGovUkDate(context.reportingPeriodEnd)}`}
                metadata="ZIP"
                onDownload={() => downloadMutation.mutate()}
                isLoading={downloadMutation.isPending}
                errorMessage={downloadError}
                downloadTestId="la-maturity-submitted-download"
                errorTestId="la-maturity-submitted-download-error"
                testId="la-maturity-submitted-download-card"
              />
            </section>

            <div className="maturity-submitted__actions">
              <GovUKButton onClick={() => navigate(homeLinkForRole(userRole))} data-testid="la-maturity-submitted-home">
                Go to homepage
              </GovUKButton>
            </div>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default Submitted;
