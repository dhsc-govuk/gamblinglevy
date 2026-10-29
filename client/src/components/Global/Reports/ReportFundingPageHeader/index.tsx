import React from 'react';
import { GovUKInsetText, H1, SummaryList } from '../../../GovUKComponents';
import './styles.css';

interface Props {
  heading: string;
  reportingPeriod: React.ReactNode;
}

// Shared header for the "Record funding spent" pages (BLUM-6612): section caption, page heading and the
// reporting-period context in the Figma summary inset. There is deliberately no inline-paragraph option:
// the bold "Reporting period: …" line it used to render is the defect the Figma review raised against every
// screen in this family (sections 12, 13 and 15), so it is not something a new screen should be able to opt
// into by omitting a prop.
const ReportFundingPageHeader = ({ heading, reportingPeriod }: Props): React.ReactElement => (
  <>
    <span className="govuk-caption-l">Record funding spent</span>
    <H1>{heading}</H1>
    <GovUKInsetText className="report-funding-page-header__details" data-testid="reports-funding-spent-context-inset">
      <SummaryList halfWidthColumns items={[{ label: 'Reporting period', value: reportingPeriod, testId: 'reports-funding-spent-reporting-period' }]} />
    </GovUKInsetText>
  </>
);

export default ReportFundingPageHeader;
