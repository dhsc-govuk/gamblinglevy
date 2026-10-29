import React from 'react';
import { GovUKInsetText, H1, SummaryList } from '../../../GovUKComponents';
import './styles.css';

interface Props {
  heading: string;
  reportingPeriod: React.ReactNode;
}

// Shared header for the "Provide strategic feedback" pages (BLUM-6613): section caption, page heading and the
// reporting-period context in the Figma summary inset. Mirrors ReportFundingPageHeader (caption differs, so
// it is its own component), including having no inline-paragraph option — see the note there.
const ReportStrategicFeedbackPageHeader = ({ heading, reportingPeriod }: Props): React.ReactElement => (
  <>
    <span className="govuk-caption-l">Provide strategic feedback</span>
    <H1>{heading}</H1>
    <GovUKInsetText className="report-strategic-feedback-page-header__details" data-testid="reports-strategic-feedback-context-inset">
      <SummaryList halfWidthColumns items={[{ label: 'Reporting period', value: reportingPeriod, testId: 'reports-strategic-feedback-reporting-period' }]} />
    </GovUKInsetText>
  </>
);

export default ReportStrategicFeedbackPageHeader;
