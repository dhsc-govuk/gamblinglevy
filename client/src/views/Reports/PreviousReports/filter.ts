import type { FilterOption } from '../../../components/GovUKComponents';
import type { ReportHistoryRow } from '../display';

// One "Filter by" select over two fields (BLUM-9088): each option says which field it matches, and GovUKTable applies
// it through FilterOption.matches. The value is the raw field value. The type values are the ones the select offered
// before this, which QA automation selects by, and no type value equals a status value.
const option = <K extends 'type' | 'status'>(field: K, value: NonNullable<ReportHistoryRow[K]>, label: string): FilterOption<ReportHistoryRow> => ({
  value,
  label,
  matches: (row) => row[field] === value,
});

// Labels are the ticket's copy. "Submitted" is the LA wording of the `completed` status and must stay in step with
// LA_SUBMISSION_STATUS_LABEL, which labels the row's tag. No other status is offered because none reaches this list:
// reportHistoryRows keeps only submitted and overdue periods.
export const REPORT_HISTORY_FILTERS: FilterOption<ReportHistoryRow>[] = [
  option('type', 'quarterly_report', 'Type: Quarterly report'),
  option('type', 'maturity_assessment', 'Type: Maturity assessment'),
  option('status', 'completed', 'Status: Submitted'),
  option('status', 'overdue', 'Status: Overdue'),
];
