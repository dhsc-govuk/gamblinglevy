import React from 'react';
import { H1, Paragraph } from '../../../GovUKComponents';

interface Props {
  heading: string;
  reportingPeriod: string;
  activityTitle: string;
  note?: string;
}

// Shared header for the "Enter activity key results" pages (BLUM-6611): section caption, page heading
// and the reporting-period / activity context lines.
const ReportActivityPageHeader = ({ heading, reportingPeriod, activityTitle, note }: Props): React.ReactElement => (
  <>
    <span className="govuk-caption-l">Enter activity key results</span>
    <H1>{heading}</H1>
    <Paragraph data-testid="reports-key-results-reporting-period">
      <strong>Reporting period:</strong> {reportingPeriod}
    </Paragraph>
    <Paragraph data-testid="reports-key-results-activity">
      <strong>Activity:</strong> {activityTitle}
    </Paragraph>
    {note && <Paragraph>{note}</Paragraph>}
  </>
);

export default ReportActivityPageHeader;
