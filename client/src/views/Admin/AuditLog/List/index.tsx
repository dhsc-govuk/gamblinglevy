import React, { useMemo, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useQuery } from 'react-query';
import { Link as RouterLink } from 'react-router-dom';
import { GeneralLayout } from '../../../../layouts';
import {
  GovUKFieldset,
  GovUKButton,
  H1,
} from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { auditLogsListKey } from '../../../../helpers/queriesParams';
import {
  AUDIT_ACTIONS,
  AuditLogFilterParams,
  auditActionLabel,
  getAuditLogs,
} from '../queries';

const PAGE_SIZE = 50;

const formatDate = (iso: string): string => {
  try {
    return new Date(iso).toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short' });
  } catch {
    return iso;
  }
};

const AuditLogList = (): React.ReactElement => {
  usePageTitle('Audit log');
  const [action, setAction] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [page, setPage] = useState(1);

  const filter: AuditLogFilterParams = useMemo(
    () => ({
      action: action || undefined,
      from: from ? new Date(from).toISOString() : undefined,
      to: to ? new Date(to).toISOString() : undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
    [action, from, to, page],
  );

  const filterKey = JSON.stringify(filter);
  const { data, isLoading } = useQuery(auditLogsListKey(filterKey), () => getAuditLogs(filter), {
    keepPreviousData: true,
  });

  const items = data?.data.items ?? [];
  const totalCount = data?.data.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <GeneralLayout
      breadcrumbs={[{ label: 'Home', link: '/admin/home' }]}
    >
      <div style={{ paddingTop: '20px' }}>
        <H1>Audit log</H1>
        <p className="govuk-body">
          Immutable record of account, permission, and data change events.
        </p>

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '20px', alignItems: 'flex-end', marginBottom: '20px' }} data-testid="admin-audit-log-filters">
          <div style={{ minWidth: '220px' }}>
            <GovUKFieldset.Select
              id="action"
              name="action"
              label="Event"
              value={action}
              options={AUDIT_ACTIONS}
              onChange={(e) => { setAction(e.target.value); setPage(1); }}
            />
          </div>
          <div style={{ minWidth: '180px' }}>
            <div className="govuk-form-group">
              <label className="govuk-label govuk-label--s" htmlFor="from">From</label>
              <input
                id="from"
                name="from"
                data-testid="admin-audit-log-filter-from"
                type="date"
                className="govuk-input"
                value={from}
                onChange={(e) => { setFrom(e.target.value); setPage(1); }}
              />
            </div>
          </div>
          <div style={{ minWidth: '180px' }}>
            <div className="govuk-form-group">
              <label className="govuk-label govuk-label--s" htmlFor="to">To</label>
              <input
                id="to"
                name="to"
                data-testid="admin-audit-log-filter-to"
                type="date"
                className="govuk-input"
                value={to}
                onChange={(e) => { setTo(e.target.value); setPage(1); }}
              />
            </div>
          </div>
          <div>
            <GovUKButton
              className="govuk-button govuk-button--secondary"
              onClick={() => {
                setAction('');
                setFrom('');
                setTo('');
                setPage(1);
              }}
              data-testid="admin-audit-log-clear-filters"
            >
              Clear filters
            </GovUKButton>
          </div>
        </div>

        {isLoading ? (
          <PageLoading />
        ) : items.length === 0 ? (
          <p className="govuk-body">No events match the current filters.</p>
        ) : (
          <>
            <table className="govuk-table" data-testid="admin-audit-log-table">
              <thead className="govuk-table__head">
                <tr className="govuk-table__row">
                  <th scope="col" className="govuk-table__header">When (UTC)</th>
                  <th scope="col" className="govuk-table__header">Event</th>
                  <th scope="col" className="govuk-table__header">Actor</th>
                  <th scope="col" className="govuk-table__header">Target</th>
                  <th scope="col" className="govuk-table__header">Organisation</th>
                </tr>
              </thead>
              <tbody className="govuk-table__body">
                {items.map((item) => (
                  <tr key={item.id} className="govuk-table__row" data-testid={`admin-audit-log-row-${item.id}`}>
                    <td className="govuk-table__cell">
                      <RouterLink to={`/admin/audit-logs/${item.id}`} className="govuk-link" data-testid={`admin-audit-log-row-${item.id}-open`}>
                        {formatDate(item.occurredAt)}
                      </RouterLink>
                    </td>
                    <td className="govuk-table__cell">{auditActionLabel(item.action)}</td>
                    <td className="govuk-table__cell">{item.actorDisplayName ?? item.actorUserId}</td>
                    <td className="govuk-table__cell">{item.targetDisplayName ?? `${item.targetType} (${item.targetId.slice(0, 8)}…)`}</td>
                    <td className="govuk-table__cell">{item.organisationName ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <nav className="govuk-pagination" role="navigation" aria-label="Pagination" data-testid="admin-audit-log-pagination">
              <ul className="govuk-pagination__list">
                {page > 1 && (
                  <li className="govuk-pagination__item govuk-pagination__item--prev">
                    <button
                      type="button"
                      className="govuk-pagination__link"
                      onClick={() => setPage((p) => Math.max(1, p - 1))}
                      data-testid="admin-audit-log-pagination-prev"
                    >
                      Previous
                    </button>
                  </li>
                )}
                <li className="govuk-pagination__item">
                  <span className="govuk-body">
                    Page {page} of {totalPages} — {totalCount} events
                  </span>
                </li>
                {page < totalPages && (
                  <li className="govuk-pagination__item govuk-pagination__item--next">
                    <button
                      type="button"
                      className="govuk-pagination__link"
                      onClick={() => setPage((p) => p + 1)}
                      data-testid="admin-audit-log-pagination-next"
                    >
                      Next
                    </button>
                  </li>
                )}
              </ul>
            </nav>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default AuditLogList;
