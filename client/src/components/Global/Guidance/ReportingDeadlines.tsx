import React from 'react';
import { GovUKTable, type Column } from '../../GovUKComponents';
import { slugifyTestId } from '../../../helpers/testIds';

export interface DeadlineRow {
  period: string;
  window: string;
  deadline: string;
}

const columns: Column<DeadlineRow>[] = [
  { key: 'period', header: 'Reporting period' },
  { key: 'window', header: 'Reporting window' },
  { key: 'deadline', header: 'Submission deadline' },
];

interface Props {
  rows: DeadlineRow[];
  testId?: string;
}

// The first year's phased reporting timetable. Rows are supplied per audience rather than shared here: the
// two approved drafts give different FY2026/2027 Maturity Assessment deadlines (DHSC 30 April 2026, LA
// 30 April 2027) and BLUM-7633 ships each page exactly as its own document reads.
//
// `title` is deliberately not passed — GovUKTable renders it as an <h1>, and this table sits under the
// page's "Reporting deadlines" H2, which already labels it.
//
// keyExtractor slugifies: GovUKTable feeds its return value straight into the row and cell testids, and the
// reporting period is prose ("Quarter 1 (April to June 2026)"), which would put spaces and parentheses into
// a testid the contract requires to be kebab-case ASCII.
const ReportingDeadlines = ({ rows, testId }: Props): React.ReactElement => (
  <div className="govuk-!-margin-bottom-4">
    <GovUKTable<DeadlineRow> label="Reporting deadlines" data={rows} columns={columns} keyExtractor={(row) => slugifyTestId(row.period)} searchable={false} testId={testId} />
  </div>
);

export default ReportingDeadlines;
