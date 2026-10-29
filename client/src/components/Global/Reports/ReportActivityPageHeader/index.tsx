import React from 'react';
import { GovUKInsetText, H1, Paragraph, SummaryList } from '../../../GovUKComponents';
import './styles.css';

interface Props {
  heading: string;
  reportingPeriod: React.ReactNode;
  activityTitle: string;
  note?: string;
}

// Shared header for the "Enter activity results" pages (BLUM-6611): section caption, page heading
// and the reporting-period / activity context in the Figma summary inset. No inline-paragraph option — see
// the note on ReportFundingPageHeader.
const ReportActivityPageHeader = ({ heading, reportingPeriod, activityTitle, note }: Props): React.ReactElement => (
  <>
    <span className="govuk-caption-l">Enter activity results</span>
    <H1>{heading}</H1>
    <GovUKInsetText className="report-activity-page-header__details" data-testid="reports-key-results-context-inset">
      <SummaryList
        halfWidthColumns
        items={[
          { label: 'Reporting period', value: reportingPeriod, testId: 'reports-key-results-reporting-period' },
          { label: 'Activity', value: activityTitle, testId: 'reports-key-results-activity' },
        ]}
      />
    </GovUKInsetText>
    {note && <Paragraph>{note}</Paragraph>}
  </>
);

export default ReportActivityPageHeader;
