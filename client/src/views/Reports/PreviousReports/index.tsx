import React, { useMemo } from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router-dom';
import { GeneralLayout } from '../../../layouts';
import { GovUKTable, GovUKInsetText, Paragraph, type Column, type FilterOption } from '../../../components/GovUKComponents';
import SubmissionStatusTag from '../../../components/Global/Submissions/SubmissionStatusTag';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { composeTestId } from '../../../helpers/testIds';
import { myReportCollectionsKey } from '../../../helpers/queriesParams';
import { getMyReportCollections } from '../queries';
import { formatGovUkDate, reportHistoryRows, typeLabel, LA_SUBMISSION_STATUS_LABEL, type ReportHistoryRow } from '../display';
import { previousReportsBreadcrumbs } from '../navigation';
import { REPORT_HISTORY_FILTERS } from './filter';
import './styles.css';

// Reporting period first, so an overdue row sits in date order alongside the submitted ones. Sorting by submission
// date is kept as an option but cannot be the default: an overdue row has no submission date, and GovUKTable's
// comparator returns 0 for those pairs, which would scatter them through the table (BLUM-7077).
const sortOptions: FilterOption[] = [
  { value: 'reportingPeriodStart-desc', label: 'Reporting period, latest first' },
  { value: 'reportingPeriodStart-asc', label: 'Reporting period, earliest first' },
  { value: 'submittedAt-asc', label: 'Submission date, earliest first' },
  { value: 'submittedAt-desc', label: 'Submission date, latest first' },
  { value: 'displayName-asc', label: 'Name (A to Z)' },
  { value: 'displayName-desc', label: 'Name (Z to A)' },
];

// The design (Figma 420-80855) sized three columns at a third each of the 960px table. With the Status column added
// for BLUM-7077 the explicit widths go to the three columns holding long content — Name, Type and the "31 December
// 2026" date — leaving the remainder to Status, whose tags are short enough not to need it.
const columns: Column<ReportHistoryRow>[] = [
  {
    key: 'displayName',
    header: 'Name',
    headerClassName: 'govuk-!-width-one-third',
    render: (row) => (
      <RouterLink to={`/reports/submitted/${row.collectionId}`} className="govuk-link" data-testid={composeTestId('la-reports-submitted', 'row', row.collectionId, 'name')}>
        {row.displayName}
      </RouterLink>
    ),
  },
  {
    key: 'type',
    header: 'Type',
    headerClassName: 'govuk-!-width-one-quarter',
    render: (row) => typeLabel(row.type),
  },
  {
    key: 'status',
    header: 'Status',
    // LA wording: a finalised report reads "Submitted" rather than the DHSC screens' "Completed". Colours are shared.
    render: (row) => (
      <SubmissionStatusTag status={row.status} label={LA_SUBMISSION_STATUS_LABEL[row.status]} testId={composeTestId('la-reports-submitted', 'row', row.collectionId, 'status')} />
    ),
  },
  {
    key: 'submittedAt',
    header: 'Submitted on',
    headerClassName: 'govuk-!-width-one-quarter',
    // An unsubmitted (overdue) row has no date — formatGovUkDate renders the em dash.
    render: (row) => formatGovUkDate(row.submittedAt),
  },
];

// LA report history (BLUM-6097). The caller's own finished reporting periods — submitted reports plus any window
// that closed without a submission, which shows as Overdue (BLUM-7077) — with search, sort and a type/status filter
// (BLUM-9088).
// Org-scoped server-side.
const PreviousReports = (): React.ReactElement => {
  usePageTitle('Your reports');
  const { userRole } = useAuthProvider();

  const { data, isLoading } = useQuery({ queryKey: myReportCollectionsKey, queryFn: getMyReportCollections });
  const rows = useMemo(() => reportHistoryRows(data?.data ?? []), [data]);

  return (
    // The design (Figma 420-80855) omits the beta phase banner, but that is now global chrome on every page per
    // BLUM-7060 AC1 (see BLUM-7102).
    <GeneralLayout breadcrumbs={previousReportsBreadcrumbs(userRole)}>
      <div className="la-report-history" data-testid="la-reports-submitted-page">
        {/* Heading sizes and the two-thirds intro column per the design (Figma 420-80855): 36px H1, 24px H2. */}
        <div className="govuk-grid-row">
          <div className="govuk-grid-column-two-thirds">
            <h1 className="govuk-heading-l">Your reports</h1>
            {/* Both clauses matter: a submitted report is immutable immediately (Submission.Submit locks it, even
                while the window is still open), and an unsubmitted period can no longer be worked on once closed. */}
            <Paragraph>Select a report to view it. You cannot edit a report once you have submitted it, or after its data collection has closed.</Paragraph>
          </div>
        </div>

        <h2 className="govuk-heading-m">Report history</h2>

        {isLoading ? (
          <PageLoading />
        ) : rows.length === 0 ? (
          <GovUKInsetText data-testid="la-reports-submitted-empty">
            You do not have any past reports yet. Reports will appear here once you submit one or a data collection closes.
          </GovUKInsetText>
        ) : (
          <GovUKTable<ReportHistoryRow>
            label="Report history"
            testId="la-reports-submitted"
            data={rows}
            columns={columns}
            keyExtractor={(row) => row.collectionId}
            searchable
            searchPlaceholder="Search by name"
            searchField="displayName"
            sortOptions={sortOptions}
            sortLabel="Sort by"
            filterOptions={REPORT_HISTORY_FILTERS}
            filterLabel="Filter by"
            filterPlaceholder="Select"
            noResultsMessage="No reports match your search or filter."
          />
        )}

        <Paragraph>
          <RouterLink to="/reports" className="govuk-link" data-testid="la-reports-submitted-return">
            Return to open data collections
          </RouterLink>
        </Paragraph>
      </div>
    </GeneralLayout>
  );
};

export default PreviousReports;
