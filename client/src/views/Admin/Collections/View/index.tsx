import React from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { useQuery } from 'react-query';
import { ManagementLayout } from '../../../../layouts';
import {
  GovUKActionMenu,
  GovUKNotificationBanner,
  GovUKSummaryCard,
  SummaryList,
  H1,
  Paragraph,
  LoadingBox,
  type ActionMenuItem,
  type SummaryCardAction,
} from '../../../../components/GovUKComponents';
import usePageTitle, { pageTitleWithName } from '../../../../hooks/usePageTitle';
import { composeTestId } from '../../../../helpers/testIds';
import { collectionKey } from '../../../../helpers/queriesParams';
import { collectionBreadcrumbs } from '../navigation';
import { getCollection } from '../queries';
import { financialYearLabel, formatGovUkDate, quarterLabel, reminderLabel, typeLabel, yesNoLabel } from '../display';
import './styles.css';

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

const formatRecordDateTime = (iso: string | null): string => {
  if (!iso) {
    return '—';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  const day = date.getDate();
  const month = MONTHS[date.getMonth()];
  const year = date.getFullYear();
  const hours = date.getHours().toString().padStart(2, '0');
  const minutes = date.getMinutes().toString().padStart(2, '0');

  return `${day} ${month} ${year}, ${hours}:${minutes}`;
};

// Shared by the <h1> and the page title so the two cannot drift apart (the reportPageTitle precedent).
const NOT_FOUND_HEADING = 'Data collection not found';

const recordValue = (name: string | null, date: string | null): React.ReactNode => (
  <>
    {name || '—'}
    <br />
    <span className="collections-view-page__record-date">{formatRecordDateTime(date)}</span>
  </>
);

const CollectionsView = (): React.ReactElement => {
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const banner = ((location.state as { banner?: { title: string; body: string } } | null) ?? {}).banner;

  const { data, isLoading } = useQuery(collectionKey(collectionId), () => getCollection(collectionId), {
    enabled: !!collectionId,
  });
  const c = data?.data;

  // The title must describe the page that is actually rendered, not the record that was requested: passing a falsy
  // name leaves the PREVIOUS page's title in the tab, so the loading and not-found branches each need their own
  // (WCAG 2.2 SC 2.4.2).
  usePageTitle(c ? pageTitleWithName('Data collection', c.name) : isLoading ? 'Data collection' : NOT_FOUND_HEADING);

  const breadcrumbs = collectionBreadcrumbs([{ label: 'Data collections', link: '/admin/collections' }]);

  if (isLoading) {
    return (
      <ManagementLayout breadcrumbs={breadcrumbs}>
        <LoadingBox loading>
          <div style={{ paddingTop: '20px', minHeight: '200px' }} />
        </LoadingBox>
      </ManagementLayout>
    );
  }

  if (!c) {
    return (
      <ManagementLayout breadcrumbs={breadcrumbs}>
        <div style={{ paddingTop: '20px' }} data-testid="admin-collections-view-not-found">
          <H1 className="collections-view-page__title">{NOT_FOUND_HEADING}</H1>
          <Paragraph>This data collection was not found. It may have been removed.</Paragraph>
        </div>
      </ManagementLayout>
    );
  }

  const isQuarterly = c.type === 'quarterly_report';

  // Per-card "Edit" action visibility:
  // every card's Edit action is available while the collection is still Draft/Scheduled. Once it's Open,
  // only "Reporting organisations" keeps its Edit action. Closed collections are fully read-only.
  // testId is pinned to "edit" explicitly (not derived from the label) so a future copy tweak doesn't
  // silently reshuffle every card's action testid.
  const isDraftOrScheduled = c.status === 'draft' || c.status === 'scheduled';
  const changeAction = (key: string, hidden: string): SummaryCardAction[] =>
    c.status !== 'closed' && (key === 'reporting' || isDraftOrScheduled)
      ? [
          {
            label: 'Edit',
            onClick: () => navigate(`/admin/collections/${collectionId}/edit?edit=${key}`),
            visuallyHiddenText: hidden,
            testId: composeTestId(`admin-collections-view-${key}`, 'action', 'edit'),
          },
        ]
      : [];

  // Actions menu — reminder/revert/close only apply while the collection is open; delete is always available here.
  const actionItems: ActionMenuItem[] = [];
  if (c.status === 'open') {
    actionItems.push(
      { label: 'Send manual reminder', href: `/admin/collections/${collectionId}/send-reminder` },
      { label: 'Revert to draft', href: `/admin/collections/${collectionId}/revert` },
      { label: 'Close data collection', href: `/admin/collections/${collectionId}/close` }
    );
  }
  actionItems.push({ label: 'Delete data collection', href: `/admin/collections/${collectionId}/delete` });

  // Card 1 — Data collection details.
  const detailItems: { label: string; value: React.ReactNode; testId?: string }[] = [
    { label: 'What is the name of this data collection?', value: c.name },
    { label: 'What is the purpose of this data collection? (optional)', value: c.purpose || '—' },
    { label: 'What is the type of this data collection?', value: typeLabel(c.type), testId: 'admin-collections-view-details-type' },
  ];
  if (isQuarterly) {
    detailItems.push(
      { label: 'Financial year', value: financialYearLabel(c.financialYear), testId: 'admin-collections-view-details-financial-year' },
      { label: 'Quarter', value: quarterLabel(c.quarter), testId: 'admin-collections-view-details-quarter' }
    );
  }

  // Card 3 — Forms (stacked names, one per line).
  const formsValue =
    c.forms.length > 0 ? (
      <>
        {c.forms.map((form, index) => (
          <React.Fragment key={form.moduleId}>
            {index > 0 && <br />}
            {form.name ?? '—'}
          </React.Fragment>
        ))}
      </>
    ) : (
      '—'
    );

  // Card 4 — Reporting organisations.
  const reportingItems: { label: string; value: React.ReactNode; testId?: string }[] = [
    { label: 'Are you requesting data from all organisations?', value: yesNoLabel(c.isSubmittedByAllLas), testId: 'admin-collections-view-reporting-all-las' },
  ];
  if (c.isSubmittedByAllLas === false) {
    reportingItems.push({
      label: 'Which organisations must submit information?',
      testId: 'admin-collections-view-reporting-la-list',
      value:
        c.localAuthorities.length > 0 ? (
          <>
            {c.localAuthorities.map((la, index) => (
              <React.Fragment key={la.id}>
                {index > 0 && <br />}
                {la.name ?? '—'}
              </React.Fragment>
            ))}
          </>
        ) : (
          '—'
        ),
    });
  }

  // Card 5 — Automatic reminders (one row per reminder, or a single em-dash row when none).
  const reminderItems =
    c.reminders.length > 0
      ? c.reminders.map((reminder, index) => ({
          label: 'When do you want to send the reminder?',
          value: reminderLabel(reminder),
          testId: `admin-collections-view-reminders-row-${index}`,
        }))
      : [{ label: 'When do you want to send the reminder?', value: '—' }];

  // Card 6 — Record information.
  const createdValue = recordValue(c.createdBy, c.createdAt);
  const lastChangedValue = c.updatedAt ? recordValue(c.updatedBy, c.updatedAt) : '—';

  return (
    <ManagementLayout breadcrumbs={breadcrumbs}>
      <div className="collections-view-page">
        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="admin-collections-view-banner">
            <p className="govuk-body">{banner.body}</p>
          </GovUKNotificationBanner>
        )}
        <div className="collections-view-page__header">
          <H1 className="collections-view-page__title">{c.name}</H1>
          <GovUKActionMenu testId="admin-collections-view-actions" buttonLabel="Actions" items={actionItems} />
        </div>

        <GovUKSummaryCard title="Data collection details" testId="admin-collections-view-details" actions={changeAction('details', 'data collection details')}>
          <SummaryList items={detailItems} />
        </GovUKSummaryCard>

        <GovUKSummaryCard title="Collection window" testId="admin-collections-view-window" actions={changeAction('window', 'collection window')}>
          <SummaryList
            items={[
              {
                label: 'Is the data collection window the same as the reporting period?',
                value: yesNoLabel(c.windowSameAsReportingPeriod),
                testId: 'admin-collections-view-window-same-as-period',
              },
              { label: 'When will the data collection open?', value: formatGovUkDate(c.opensDate), testId: 'admin-collections-view-window-opens' },
              { label: 'What is the deadline for submissions?', value: formatGovUkDate(c.deadline), testId: 'admin-collections-view-window-deadline' },
            ]}
          />
        </GovUKSummaryCard>

        <GovUKSummaryCard title="Forms" testId="admin-collections-view-forms" actions={changeAction('forms', 'forms')}>
          <SummaryList items={[{ label: 'Which forms do you want to include in this data collection?', value: formsValue }]} />
        </GovUKSummaryCard>

        <GovUKSummaryCard title="Reporting organisations" testId="admin-collections-view-reporting" actions={changeAction('reporting', 'reporting organisations')}>
          <SummaryList items={reportingItems} />
        </GovUKSummaryCard>

        <GovUKSummaryCard title="Automatic reminders" testId="admin-collections-view-reminders" actions={changeAction('reminders', 'automatic reminders')}>
          <SummaryList items={reminderItems} />
        </GovUKSummaryCard>

        <GovUKSummaryCard title="Record information" testId="admin-collections-view-record">
          <SummaryList
            items={[
              { label: 'Created', value: createdValue },
              { label: 'Last changed', value: lastChangedValue },
            ]}
          />
        </GovUKSummaryCard>
      </div>
    </ManagementLayout>
  );
};

export default CollectionsView;
