import React from 'react';
import { H1, Paragraph } from '../../../GovUKComponents';

interface Props {
  heading: string;
  reportingPeriod: string;
}

// Shared header for the "Record funding spent" pages (BLUM-6612): section caption, page heading and the
// reporting-period line. Unlike ReportActivityPageHeader there is no per-activity context line.
const ReportFundingPageHeader = ({ heading, reportingPeriod }: Props): React.ReactElement => (
  <>
    <span className="govuk-caption-l">Record funding spent</span>
    <H1>{heading}</H1>
    <Paragraph data-testid="reports-funding-spent-reporting-period">
      <strong>Reporting period:</strong> {reportingPeriod}
    </Paragraph>
  </>
);

export default ReportFundingPageHeader;
