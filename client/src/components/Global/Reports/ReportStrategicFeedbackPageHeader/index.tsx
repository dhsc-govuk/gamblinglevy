import React from 'react';
import { H1, Paragraph } from '../../../GovUKComponents';

interface Props {
  heading: string;
  reportingPeriod: string;
}

// Shared header for the "Provide strategic feedback" pages (BLUM-6613): section caption, page heading and the
// reporting-period line. Mirrors ReportFundingPageHeader (caption differs, so it is its own component).
const ReportStrategicFeedbackPageHeader = ({ heading, reportingPeriod }: Props): React.ReactElement => (
  <>
    <span className="govuk-caption-l">Provide strategic feedback</span>
    <H1>{heading}</H1>
    <Paragraph data-testid="reports-strategic-feedback-reporting-period">
      <strong>Reporting period:</strong> {reportingPeriod}
    </Paragraph>
  </>
);

export default ReportStrategicFeedbackPageHeader;
