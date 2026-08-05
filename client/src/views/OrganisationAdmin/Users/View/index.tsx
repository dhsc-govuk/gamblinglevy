import React from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, useParams, useLocation } from 'react-router-dom';
import { OrganisationManagementLayout } from '../../../../layouts';
import { GovUKActionMenu, GovUKNotificationBanner, GovUKSummaryCard, NotAuthorised, SummaryList, H1 } from '../../../../components/GovUKComponents';
import { ROLE_LABELS, useAuthProvider } from '../../../../components/AuthProvider';
import { canManageUsers } from '../../navigation';
import usePageTitle from '../../../../hooks/usePageTitle';
import { UserLoadFailed, useOrganisationUser, organisationUserPageTitle } from '../../../../components/Global/OrganisationUsers';

interface BannerState {
  banner?: { title: string; body: string };
}

const formatDateTime = (iso: string): string => {
  const d = new Date(iso);
  const date = d.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
  const time = d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
  return `${date}, ${time}`;
};

const UsersView = (): React.ReactElement => {
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const { userId: actorId, userRole } = useAuthProvider();
  const canManage = canManageUsers(userRole);
  const banner = ((location.state as BannerState | null) ?? {}).banner;

  const userQuery = useOrganisationUser(userId);
  const { user, isDenied, loadFailed, isLoading } = userQuery;

  usePageTitle(organisationUserPageTitle(userQuery, 'User'));

  // Reached by hand-editing the URL to another LA's user id. NotAuthorised renders its own <main>, so it replaces the
  // layout rather than sitting inside it — otherwise the page would carry two main landmarks.
  if (isDenied) return <NotAuthorised goBack={navigate} testId="organisation-admin-users-view-not-authorised" />;

  const isSelf = actorId !== null && user?.id === actorId;
  const isActive = user?.status === 'active';

  // An LA lead views colleagues read-only, so it gets no lifecycle actions and no Edit (BLUM-7038 AC3).
  // The backend gates these to Role.UserManagers regardless; this only removes the affordances.
  const actionItems = !canManage
    ? []
    : [
        {
          label: 'Reset MFA',
          onClick: () => navigate(`/organisation-admin/users/${userId}/reset-mfa`),
          testId: 'organisation-admin-users-view-action-reset-mfa',
        },
        ...(isActive && !isSelf
          ? [
              {
                label: 'Deactivate account',
                onClick: () => navigate(`/organisation-admin/users/${userId}/deactivate`),
                testId: 'organisation-admin-users-view-action-deactivate',
              },
            ]
          : []),
        ...(!isActive
          ? [
              {
                label: 'Reactivate account',
                onClick: () => navigate(`/organisation-admin/users/${userId}/deactivate`),
                testId: 'organisation-admin-users-view-action-reactivate',
              },
            ]
          : []),
        ...(!isSelf
          ? [
              {
                label: 'Delete account',
                onClick: () => navigate(`/organisation-admin/users/${userId}/delete`),
                testId: 'organisation-admin-users-view-action-delete',
              },
            ]
          : []),
      ];

  return (
    <OrganisationManagementLayout
      breadcrumbs={[
        { label: 'Home', link: '/organisation-admin/home' },
        { label: 'Manage account', link: '/organisation-admin/users' },
        { label: 'Users', link: '/organisation-admin/users' },
      ]}>
      {banner && (
        <GovUKNotificationBanner type="success" title={banner.title} testId="organisation-admin-users-view-banner">
          <p className="govuk-body">{banner.body}</p>
        </GovUKNotificationBanner>
      )}

      {loadFailed ? (
        <UserLoadFailed testId="organisation-admin-users-view-load-error" />
      ) : isLoading || !user ? (
        <PageLoading />
      ) : (
        <div className="organisation-admin-user-view">
          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              flexWrap: 'wrap',
              gap: '16px',
              marginBottom: '20px',
            }}>
            <H1 style={{ marginBottom: 0 }}>
              {user.firstName} {user.lastName}
            </H1>
            {actionItems.length > 0 && <GovUKActionMenu buttonLabel="Actions" items={actionItems} testId="organisation-admin-users-view-actions" />}
          </div>

          <GovUKSummaryCard
            title="User details"
            actions={
              canManage
                ? [
                    {
                      label: 'Edit',
                      onClick: () => navigate(`/organisation-admin/users/${userId}/edit`),
                      visuallyHiddenText: 'user details',
                      testId: 'organisation-admin-users-view-edit',
                    },
                  ]
                : []
            }
            testId="organisation-admin-users-view-details-card">
            <SummaryList
              testId="organisation-admin-users-view-details"
              items={[
                { label: 'First name', value: user.firstName },
                { label: 'Last name', value: user.lastName },
                { label: 'Access level', value: ROLE_LABELS[user.role] ?? user.role },
                {
                  label: 'Email address',
                  value: (
                    <a href={`mailto:${user.email}`} className="govuk-link" data-testid="organisation-admin-users-view-email-link">
                      {user.email}
                    </a>
                  ),
                },
              ]}
            />
          </GovUKSummaryCard>

          <GovUKSummaryCard title="Sign in security" testId="organisation-admin-users-view-security-card">
            <SummaryList
              testId="organisation-admin-users-view-security"
              items={[
                { label: 'User access', value: isActive ? 'Active' : 'Inactive' },
                {
                  label: 'Multi-factor authentication (MFA)',
                  value: user.mfaStatus === 'enabled' ? 'Enabled' : 'Not set up yet',
                },
              ]}
            />
          </GovUKSummaryCard>

          <GovUKSummaryCard title="Account history" testId="organisation-admin-users-view-history-card">
            <SummaryList
              testId="organisation-admin-users-view-history"
              items={[
                {
                  label: 'Created',
                  value: user.createdBy ? (
                    <>
                      {user.createdBy.actorName}
                      <br />
                      {formatDateTime(user.createdBy.occurredAt)}
                    </>
                  ) : (
                    '-'
                  ),
                },
                {
                  label: 'Last updated',
                  value: user.lastUpdatedBy ? (
                    <>
                      {user.lastUpdatedBy.actorName}
                      <br />
                      {formatDateTime(user.lastUpdatedBy.occurredAt)}
                    </>
                  ) : (
                    '-'
                  ),
                },
              ]}
            />
          </GovUKSummaryCard>

          {/* Only meaningful to a role that otherwise has these actions — an LA lead has no Actions menu
              at all, so the hint would explain a restriction on affordances it never sees. */}
          {isSelf && canManage && <p className="govuk-hint govuk-!-margin-top-4">You cannot deactivate or delete your own account.</p>}
        </div>
      )}
    </OrganisationManagementLayout>
  );
};

export default UsersView;
