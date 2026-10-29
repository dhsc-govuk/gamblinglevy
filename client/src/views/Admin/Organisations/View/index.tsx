import React from 'react';
import { composeTestId } from '../../../../helpers/testIds';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, useParams, useLocation, Link as RouterLink } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKActionMenu, GovUKNotificationBanner, GovUKSummaryCard, GovUKTable, H1, H2, NameLink, SummaryList, type Column } from '../../../../components/GovUKComponents';
import { ROLE_LABELS } from '../../../../components/AuthProvider';
import usePageTitle, { pageTitleWithName } from '../../../../hooks/usePageTitle';
import { localAuthorityKey, organisationUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { formatGBP } from '../../../../helpers/currency';
import { getLocalAuthority } from '../queries';
import { displayOrganisationStatus } from '../display';
import { getOrganisationUserList } from '../../../../components/Global/OrganisationUsers/api';
import './styles.css';

interface BannerState {
  banner?: { title: string; body: string };
}

interface OrganisationUserRow {
  id: string;
  name: string;
  role: string;
  status: string;
}

const sentenceCase = (value: string) => (value ? `${value.charAt(0).toUpperCase()}${value.slice(1)}` : value);

const canReactivateStatus = (status: string) => status === 'suspended' || status === 'deactivated';

const formatDateTime = (iso: string | null) => {
  if (!iso) return '—';

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';

  const formattedDate = date.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
  const formattedTime = date.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
  return `${formattedDate}, ${formattedTime}`;
};

const LocalAuthoritiesView = (): React.ReactElement => {
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const banner = ((location.state as BannerState | null) ?? {}).banner;

  const { data, isLoading } = useQuery({ queryKey: localAuthorityKey(organisationId), queryFn: () => getLocalAuthority(organisationId), enabled: !!organisationId });
  const { data: usersData, isLoading: isLoadingUsers } = useQuery({
    queryKey: organisationUsersListKey(organisationId),
    queryFn: () => getOrganisationUserList(organisationId),
    enabled: !!organisationId,
  });

  const org = data?.data;
  usePageTitle(pageTitleWithName('Organisation details', org?.name));
  const users = usersData?.data ?? [];
  const userRows: OrganisationUserRow[] = users.map((user) => ({
    id: user.id,
    name: `${user.firstName} ${user.lastName}`,
    role: ROLE_LABELS[user.role] ?? user.role,
    status: sentenceCase(user.status),
  }));

  const canSuspend = org && (org.onboardingStatus === 'active' || org.onboardingStatus === 'invited');
  const canReactivate = org && canReactivateStatus(org.onboardingStatus);

  const actionItems = org
    ? [
        ...(canSuspend ? [{ label: 'Deactivate account', href: ROUTES.admin.organisations.suspend(org.id), testId: 'admin-local-authorities-view-action-deactivate' }] : []),
        ...(canReactivate ? [{ label: 'Reactivate account', href: ROUTES.admin.organisations.reinstate(org.id), testId: 'admin-local-authorities-view-action-reactivate' }] : []),
        { label: 'Delete account', href: ROUTES.admin.organisations.delete(org.id), testId: 'admin-local-authorities-view-action-delete' },
      ]
    : [];

  const contactUsers = users.slice(0, 2);

  const userColumns: Column<OrganisationUserRow>[] = [
    {
      key: 'name',
      header: 'Name',
      render: (user) => (
        <NameLink
          name={user.name}
          href={ROUTES.admin.organisations.user(organisationId, user.id)}
          testId={composeTestId('admin-local-authorities-view-users-table', 'row', user.id, 'name-link')}
        />
      ),
    },
    { key: 'role', header: 'Role' },
    {
      key: 'status',
      header: 'Status',
      align: 'right',
      cellStyle: { width: '124px' },
      headerStyle: { width: '124px' },
    },
  ];

  return (
    <ManagementLayout
      breadcrumbs={[
        { label: 'Home', link: ROUTES.admin.home },
        { label: 'Management', link: ROUTES.admin.organisations.list },
        { label: 'Organisations', link: ROUTES.admin.organisations.list },
      ]}>
      <div className={['organisations-view-page', banner ? 'organisations-view-page--has-banner' : ''].filter(Boolean).join(' ')}>
        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="admin-local-authorities-view-banner">
            <p className="govuk-body">{banner.body}</p>
          </GovUKNotificationBanner>
        )}
        {isLoading || !org ? (
          <PageLoading />
        ) : (
          <>
            <div className="organisations-view-page__header">
              <H1 className="organisations-view-page__title">{org.name}</H1>
              {actionItems.length > 0 && <GovUKActionMenu buttonLabel="Actions" items={actionItems} testId="admin-local-authorities-view-actions" />}
            </div>

            <GovUKSummaryCard
              title="Organisation details"
              actions={[
                {
                  label: 'Edit',
                  onClick: () => navigate(ROUTES.admin.organisations.edit(org.id)),
                  visuallyHiddenText: 'organisation details',
                  testId: 'admin-local-authorities-view-edit',
                },
              ]}
              testId="admin-local-authorities-view-details-card">
              <SummaryList
                noOuterBorder
                items={[
                  { label: 'Name', value: org.name },
                  { label: 'ONS code', value: org.onsCode ?? '—' },
                  { label: 'Region', value: org.region },
                  { label: 'Type', value: org.type },
                  { label: 'E-code', value: org.eCode || '—' },
                  // One row per allocation, each naming its financial year (BLUM-7608). Before this the single
                  // row showed a bare amount: the year was rendered only when the API supplied it, and it never
                  // did, so the figure on screen did not say which year it belonged to.
                  {
                    label: 'Annual allocation',
                    value: (
                      <>
                        {formatGBP(org.allocatedAmount)}
                        {org.fundingFinancialYear && <span className="organisations-view-page__muted"> ({org.fundingFinancialYear})</span>}
                      </>
                    ),
                  },
                  {
                    label: 'Annual allocation (upcoming year)',
                    value: (
                      <>
                        {formatGBP(org.nextYearAllocatedAmount)}
                        {org.nextYearFinancialYear && <span className="organisations-view-page__muted"> ({org.nextYearFinancialYear})</span>}
                      </>
                    ),
                  },
                ]}
                testId="admin-local-authorities-view-details"
              />
            </GovUKSummaryCard>

            <GovUKSummaryCard
              title="Contacts"
              actions={[
                {
                  label: 'Edit',
                  onClick: () => navigate(ROUTES.admin.organisations.editContacts(org.id)),
                  visuallyHiddenText: 'contacts',
                  testId: 'admin-local-authorities-view-edit-contacts',
                },
              ]}
              testId="admin-local-authorities-view-contacts-card">
              <SummaryList
                noOuterBorder
                items={
                  contactUsers.length > 0
                    ? contactUsers.map((user, index) => ({
                        label: `Contact person ${index + 1}`,
                        value: (
                          <span className="organisations-view-page__stacked-value">
                            <span>
                              {user.firstName} {user.lastName}
                            </span>
                            <span className="organisations-view-page__muted">{ROLE_LABELS[user.role] ?? user.role}</span>
                            <a href={`mailto:${user.email}`} className="govuk-link" data-testid={composeTestId('admin-local-authorities-view', 'contact', index + 1, 'email')}>
                              {user.email}
                            </a>
                          </span>
                        ),
                      }))
                    : [{ label: 'Contact person 1', value: 'No contact people have been added.' }]
                }
                testId="admin-local-authorities-view-contacts"
              />
            </GovUKSummaryCard>

            <GovUKSummaryCard title="User access" testId="admin-local-authorities-view-access-card">
              <SummaryList
                noOuterBorder
                items={[
                  {
                    label: 'Status',
                    value: displayOrganisationStatus(org.onboardingStatus),
                  },
                ]}
                testId="admin-local-authorities-view-access"
              />
            </GovUKSummaryCard>

            <GovUKSummaryCard title="Record information" testId="admin-local-authorities-view-record-card">
              <SummaryList
                noOuterBorder
                items={[
                  { label: 'Created', value: <span className="organisations-view-page__muted">{formatDateTime(org.createdAt)}</span> },
                  { label: 'Last updated', value: <span className="organisations-view-page__muted">{formatDateTime(org.updatedAt)}</span> },
                ]}
                testId="admin-local-authorities-view-record"
              />
            </GovUKSummaryCard>

            <div className="organisations-view-page__users-header">
              <H2 className="organisations-view-page__users-title">Users</H2>
              <RouterLink to={ROUTES.admin.organisations.createUser(org.id)} className="govuk-button govuk-button--secondary" data-testid="admin-local-authorities-view-add-user">
                Add user
              </RouterLink>
            </div>

            {isLoadingUsers ? (
              <p className="govuk-body">Loading users…</p>
            ) : users.length === 0 ? (
              <div className="govuk-inset-text">You have not added any users for this organisation. When you add a user, their details will be listed on this page.</div>
            ) : (
              <GovUKTable<OrganisationUserRow>
                label="Users"
                testId="admin-local-authorities-view-users-table"
                data={userRows}
                columns={userColumns}
                keyExtractor={(user) => user.id}
                getRowHref={(user) => ROUTES.admin.organisations.user(org.id, user.id)}
                searchPlaceholder="Search by name"
                searchField="name"
                sortLabel="Sort by"
                sortOptions={[
                  { value: 'name-asc', label: 'Name A-Z' },
                  { value: 'name-desc', label: 'Name Z-A' },
                ]}
              />
            )}
          </>
        )}
      </div>
    </ManagementLayout>
  );
};

export default LocalAuthoritiesView;
