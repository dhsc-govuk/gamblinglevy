import React, { useMemo, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router-dom';
import { ManagementLayout } from '../../../../layouts';
import { GovUKFieldset, GovUKTableScroll, H1, Paragraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { auditLogsListKey } from '../../../../helpers/queriesParams';
import { composeTestId } from '../../../../helpers/testIds';
import { ROUTES } from '../../../../helpers/routes';
import { AUDIT_ACTIONS, AUDIT_LOG_SORT_OPTIONS, AuditLogFilterParams, AuditLogSort, auditActionLabel, getAuditLogs } from '../queries';
import './styles.css';

const PAGE_SIZE = 50;

// Shared by the visible empty state and the live region, so the two cannot drift.
const EMPTY_MESSAGE = 'No actions match the current filter.';

// timeZone is explicit because the column header promises UTC. Without it toLocaleString renders in the
// viewer's zone, so through BST this list showed every event an hour later than the detail page — which
// formats the same field with toUTCString — while both claimed to be UTC.
const formatDate = (iso: string): string => {
  try {
    return new Date(iso).toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short', timeZone: 'UTC' });
  } catch {
    return iso;
  }
};

const AuditLogList = (): React.ReactElement => {
  usePageTitle('Audit log');
  const [action, setAction] = useState('');
  const [sort, setSort] = useState<AuditLogSort>('newest');
  const [page, setPage] = useState(1);

  const filter: AuditLogFilterParams = useMemo(
    () => ({
      action: action || undefined,
      sort,
      page,
      pageSize: PAGE_SIZE,
    }),
    [action, sort, page]
  );

  const filterKey = JSON.stringify(filter);
  const { data, isLoading, isPlaceholderData } = useQuery({ queryKey: auditLogsListKey(filterKey), queryFn: () => getAuditLogs(filter), placeholderData: keepPreviousData });

  const items = data?.data.items ?? [];
  const totalCount = data?.data.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const resultsAnnouncement =
    isLoading || isPlaceholderData
      ? ''
      : [
          totalCount === 0 ? EMPTY_MESSAGE : `${totalCount} ${totalCount === 1 ? 'action' : 'actions'} found.`,
          `Filter by action: ${AUDIT_ACTIONS.find((a) => a.value === action)?.label ?? action}.`,
          `Sort by: ${AUDIT_LOG_SORT_OPTIONS.find((o) => o.value === sort)?.label ?? sort}.`,
        ].join(' ');

  return (
    <ManagementLayout
      breadcrumbs={[
        { label: 'Home', link: ROUTES.admin.home },
        { label: 'Management', link: ROUTES.admin.management },
      ]}>
      <div className="audit-log-list-page">
        <H1 size="l" className="audit-log-list-page__title">
          Audit log
        </H1>

        <div className="audit-log-list-page__intro">
          <Paragraph>You can see a log of actions relating to managing user accounts, organisations and sessions.</Paragraph>
          <Paragraph>Select the date and time of an action to see more details.</Paragraph>
          <Paragraph>You cannot change or delete entries in the audit log.</Paragraph>
        </div>

        <div className="audit-log-list-page__filters" data-testid="admin-audit-log-filters">
          <div className="audit-log-list-page__filter">
            <GovUKFieldset.Select
              id="audit-log-action"
              name="action"
              label="Filter by action"
              labelSize="none"
              value={action}
              options={AUDIT_ACTIONS}
              testId="admin-audit-log-filter-action"
              onChange={(e) => {
                setAction(e.target.value);
                setPage(1);
              }}
            />
          </div>
          <div className="audit-log-list-page__filter">
            <GovUKFieldset.Select
              id="audit-log-sort"
              name="sort"
              label="Sort by"
              labelSize="none"
              value={sort}
              options={AUDIT_LOG_SORT_OPTIONS}
              testId="admin-audit-log-sort"
              onChange={(e) => {
                setSort(e.target.value as AuditLogSort);
                setPage(1);
              }}
            />
          </div>
        </div>

        <div className="govuk-visually-hidden" role="status" aria-live="polite" aria-atomic="true" data-testid="admin-audit-log-results-status">
          {resultsAnnouncement}
        </div>

        {isLoading ? (
          <PageLoading />
        ) : items.length === 0 ? (
          <p className="govuk-body" data-testid="admin-audit-log-empty">
            {EMPTY_MESSAGE}
          </p>
        ) : (
          <>
            <GovUKTableScroll label="Audit log" testId="admin-audit-log-table-scroll" className="audit-log-list-page__table-scroll">
              <table className="govuk-table audit-log-list-page__table" data-testid="admin-audit-log-table">
                <thead className="govuk-table__head">
                  <tr className="govuk-table__row">
                    <th scope="col" className="govuk-table__header">
                      When (UTC)
                    </th>
                    <th scope="col" className="govuk-table__header">
                      Action
                    </th>
                    <th scope="col" className="govuk-table__header">
                      User
                    </th>
                    <th scope="col" className="govuk-table__header">
                      Organisation
                    </th>
                  </tr>
                </thead>
                <tbody className="govuk-table__body">
                  {items.map((item) => {
                    const rowTestId = `admin-audit-log-row-${item.id}`;
                    return (
                      <tr key={item.id} className="govuk-table__row" data-testid={rowTestId}>
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'when')}>
                          <RouterLink to={ROUTES.admin.auditLogs.detail(item.id)} className="govuk-link" data-testid={composeTestId(rowTestId, 'open')}>
                            {formatDate(item.occurredAt)}
                          </RouterLink>
                        </td>
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'action')}>
                          {auditActionLabel(item.action)}
                        </td>
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'user')}>
                          {item.actorDisplayName ?? item.actorUserId}
                        </td>
                        <td className="govuk-table__cell" data-testid={composeTestId(rowTestId, 'cell', 'organisation')}>
                          {item.organisationName ?? '—'}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </GovUKTableScroll>

            <nav className="govuk-pagination" role="navigation" aria-label="Pagination" data-testid="admin-audit-log-pagination">
              <ul className="govuk-pagination__list">
                {page > 1 && (
                  <li className="govuk-pagination__item govuk-pagination__item--prev">
                    <button type="button" className="govuk-pagination__link" onClick={() => setPage((p) => Math.max(1, p - 1))} data-testid="admin-audit-log-pagination-prev">
                      Previous
                    </button>
                  </li>
                )}
                <li className="govuk-pagination__item">
                  <span className="govuk-body">
                    Page {page} of {totalPages} — {totalCount} actions
                  </span>
                </li>
                {page < totalPages && (
                  <li className="govuk-pagination__item govuk-pagination__item--next">
                    <button type="button" className="govuk-pagination__link" onClick={() => setPage((p) => p + 1)} data-testid="admin-audit-log-pagination-next">
                      Next
                    </button>
                  </li>
                )}
              </ul>
            </nav>
          </>
        )}
      </div>
    </ManagementLayout>
  );
};

export default AuditLogList;
