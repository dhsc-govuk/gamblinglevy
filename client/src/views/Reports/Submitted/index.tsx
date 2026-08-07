import React, { useState } from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useMutation } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import { GovUKButton, GovUKPanel, H2, Paragraph } from '../../../components/GovUKComponents';
import FileDownloadCard from '../../../components/Global/Reports/FileDownloadCard';
import { homeLinkForRole, useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { ROUTES } from '../../../helpers/routes';
import { downloadReportExport } from '../mutations';
import { downloadErrorMessage } from '../../../helpers/downloadErrors';
import { formatGovUkDate } from '../display';
import { useReportContext } from '../context';
import './styles.css';

interface SubmittedState {
  referenceNumber?: string;
}

// "Quarterly report submitted" confirmation (the final screen of the submit flow). Shows the reference number and a
// download of the finalised report. On submit the backend sends a confirmation email to the submitting user
// (BLUM-6954, SubmitReportCommandHandler). That send is best-effort (failures are logged, never blocking the submit),
// so the "confirmation email has been sent" line below is the expected-path copy, not a hard delivery guarantee.
const Submitted = (): React.ReactElement => {
  usePageTitle('Quarterly report submitted');
  const navigate = useNavigate();
  const location = useLocation();
  const { user, userRole } = useAuthProvider();
  const [downloadError, setDownloadError] = useState<string | null>(null);

  const { collectionId, context, isLoading, isError } = useReportContext();

  const downloadMutation = useMutation(() => downloadReportExport(collectionId), {
    onError: (error) => setDownloadError(downloadErrorMessage(error)),
    onSuccess: () => setDownloadError(null),
  });

  // The reference is handed over on submit (location state); on a later visit it comes from the report context.
  const stateReference = (location.state as SubmittedState | null)?.referenceNumber ?? null;
  const referenceNumber = stateReference ?? context?.referenceNumber ?? null;

  // Unknown collection, another organisation's, or a link that predates the collection-scoped flow.
  if (isError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  // Only reachable for a submitted report.
  if (!isLoading && !stateReference && (!context || !context.submittedAt)) {
    return <Navigate to={ROUTES.reports.quarterly(collectionId)} replace />;
  }

  return (
    <GeneralLayout>
      <div className="reports-submitted" data-testid="reports-submitted-page">
        {isLoading || !context ? (
          <PageLoading />
        ) : (
          <>
            <GovUKPanel className="reports-submitted__panel" title="Quarterly report submitted" data-testid="reports-submitted-panel">
              Your reference number
              <br />
              <strong data-testid="reports-submitted-reference">{referenceNumber}</strong>
            </GovUKPanel>

            <section className="reports-submitted__section" aria-labelledby="reports-submitted-next-heading">
              <H2 id="reports-submitted-next-heading">What happens next</H2>
              <Paragraph>A confirmation email has been sent to {user?.email ?? 'your registered email address'}.</Paragraph>
              <Paragraph>
                The Department of Health and Social Care (DHSC) will review your report. They&rsquo;ll contact you if they have any questions or need to discuss your submission.
              </Paragraph>
              <Paragraph>You&rsquo;ll receive an email notification when your next quarterly report is due.</Paragraph>
            </section>

            <section className="reports-submitted__section reports-submitted__download" aria-labelledby="reports-submitted-download-heading">
              <H2 id="reports-submitted-download-heading">Download your report</H2>
              <Paragraph>
                You can download a copy of your submitted report for your records. You can also select &lsquo;View previously submitted reports&rsquo; on the homepage.
              </Paragraph>

              <FileDownloadCard
                title={`Quarterly report for ${context.localAuthorityName}, ${formatGovUkDate(context.reportingPeriodStart)} to ${formatGovUkDate(context.reportingPeriodEnd)}`}
                metadata="ZIP"
                onDownload={() => downloadMutation.mutate()}
                isLoading={downloadMutation.isLoading}
                errorMessage={downloadError}
                downloadTestId="reports-submitted-download"
                errorTestId="reports-submitted-download-error"
                testId="reports-submitted-download-card"
              />
            </section>

            <div className="reports-submitted__actions">
              <GovUKButton onClick={() => navigate(homeLinkForRole(userRole))} data-testid="reports-submitted-home">
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
