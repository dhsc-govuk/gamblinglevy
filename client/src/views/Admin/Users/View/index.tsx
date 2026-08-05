import React from 'react';
import { Link as RouterLink, useNavigate, useParams, useLocation } from 'react-router-dom';
import { useQuery } from 'react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKActionMenu, GovUKNotificationBanner, GovUKSummaryCard, SummaryList, H1 } from '../../../../components/GovUKComponents';
import { useAuthProvider, ROLE_LABELS, homeLinkForRole, EnumUserRole } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { adminUserKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getUser, type AccountHistoryEntry } from '../queries';
import './styles.css';

interface BannerState {
  banner?: { title: string; body: string };
}

const formatHistoryDate = (iso: string) => {
  const d = new Date(iso);
  const date = d.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
  const time = d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
  return `${date}, ${time}`;
};

const UsersView = (): React.ReactElement => {
  usePageTitle('User');
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const { userId: actorId } = useAuthProvider();
  const banner = ((location.state as BannerState | null) ?? {}).banner;

  const { data, isLoading } = useQuery(adminUserKey(userId), () => getUser(userId), { enabled: !!userId });
  const user = data?.data;

  const isSelf = actorId !== null && user?.id === actorId;
  const isActive = user?.status === 'active';

  const actionItems = [
    { label: 'Reset MFA', onClick: () => navigate(ROUTES.admin.users.resetMfa(userId)), testId: 'admin-users-view-action-reset-mfa' },
    ...(isActive
      ? [
          {
            label: 'Deactivate account',
            onClick: () => navigate(ROUTES.admin.users.deactivate(userId)),
            disabled: isSelf,
            testId: 'admin-users-view-action-deactivate',
          },
        ]
      : [
          {
            label: 'Reactivate account',
            onClick: () => navigate(ROUTES.admin.users.reactivate(userId)),
            disabled: isSelf,
            testId: 'admin-users-view-action-reactivate',
          },
        ]),
    {
      label: 'Delete account',
      onClick: () => navigate(ROUTES.admin.users.delete(userId)),
      disabled: isSelf,
      testId: 'admin-users-view-action-delete',
    },
  ];

  const userDetailItems = user
    ? [
        { label: 'First name', value: user.firstName },
        { label: 'Last name', value: user.lastName },
        {
          label: 'Organisation',
          value:
            user.organisationId && user.organisationName ? (
              <RouterLink to={ROUTES.admin.organisations.detail(user.organisationId)} className="govuk-link" data-testid="admin-users-view-organisation-link">
                {user.organisationName}
              </RouterLink>
            ) : (
              '-'
            ),
        },
        { label: 'Access level', value: ROLE_LABELS[user.role] ?? user.role },
        {
          label: 'Email address',
          value: (
            <a href={`mailto:${user.email}`} className="govuk-link" data-testid="admin-users-view-email-link">
              {user.email}
            </a>
          ),
        },
      ]
    : [];

  const securityItems = user
    ? [
        { label: 'User access', value: isActive ? 'Active' : 'Inactive' },
        { label: 'Multi-factor authentication (MFA)', value: user.mfaStatus === 'enabled' ? 'Enabled' : 'Not set up yet' },
      ]
    : [];

  const historyValue = (entry: AccountHistoryEntry | undefined) =>
    entry ? (
      <span className="admin-users-view__history-value">
        <span>{entry.actorName}</span>
        <span>{formatHistoryDate(entry.occurredAt)}</span>
      </span>
    ) : (
      '-'
    );

  return (
    <ManagementLayout
      breadcrumbs={[
        { label: 'Home', link: homeLinkForRole(EnumUserRole.ADMIN) },
        { label: 'Management', link: ROUTES.admin.management },
        { label: 'Users', link: ROUTES.admin.users.list },
      ]}>
      {banner && (
        <div className="admin-users-view__banner">
          <GovUKNotificationBanner type="success" title={banner.title} testId="admin-users-view-banner">
            <p className="govuk-body">{banner.body}</p>
          </GovUKNotificationBanner>
        </div>
      )}

      {isLoading || !user ? null : (
        <div className="admin-users-view">
          <div className="admin-users-view__heading">
            <H1>
              {user.firstName} {user.lastName}
            </H1>
            <GovUKActionMenu buttonLabel="Actions" items={actionItems} testId="admin-users-view-actions" />
          </div>

          <GovUKSummaryCard
            title="User details"
            actions={[
              {
                label: 'Edit',
                onClick: () => navigate(ROUTES.admin.users.edit(userId)),
                visuallyHiddenText: 'user details',
                testId: 'admin-users-view-edit',
              },
            ]}
            testId="admin-users-view-details-card">
            <SummaryList items={userDetailItems} equalColumns testId="admin-users-view-details" />
          </GovUKSummaryCard>

          <GovUKSummaryCard title="Sign in security" testId="admin-users-view-security-card">
            <SummaryList items={securityItems} equalColumns testId="admin-users-view-security" />
          </GovUKSummaryCard>

          <GovUKSummaryCard title="Account history" testId="admin-users-view-history-card">
            <SummaryList
              items={[
                {
                  label: 'Created',
                  value: historyValue(user.createdBy),
                },
                {
                  label: 'Last updated',
                  value: historyValue(user.lastUpdatedBy),
                },
              ]}
              equalColumns
              testId="admin-users-view-history"
            />
          </GovUKSummaryCard>

          {isSelf && (
            <p className="govuk-hint" style={{ marginTop: '16px' }}>
              You cannot deactivate or delete your own account.
            </p>
          )}
        </div>
      )}
    </ManagementLayout>
  );
};

export default UsersView;
