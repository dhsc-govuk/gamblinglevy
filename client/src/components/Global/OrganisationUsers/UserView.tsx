import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ManagementLayout } from '../../../layouts';
import { GovUKActionMenu, GovUKSummaryCard, SummaryList, PageLoading, H1, DeleteConfirmDialog } from '../../GovUKComponents';
import { ROLE_LABELS, useAuthProvider } from '../../AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { organisationUserKey, organisationUsersListKey } from '../../../helpers/queriesParams';
import { getOrganisationUser } from './api';
import { deactivateOrganisationUser, reactivateOrganisationUser, deleteOrganisationUser } from './api';
import type { Breadcrumb } from './UsersListView';
import './UserView.css';

type Confirm = 'deactivate' | 'reactivate' | 'delete' | null;

export interface UserViewProps {
  organisationId: string;
  userId: string;
  pageTitle: string;
  breadcrumbs: Breadcrumb[];
  editHref: string;
  deleteSuccessNavigateHref: string;
  testIdPrefix: string;
}

const formatHistoryDate = (iso: string): string => {
  const d = new Date(iso);
  const date = d.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
  const time = d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
  return `${date}, ${time}`;
};

const UserView = ({ organisationId, userId, pageTitle, breadcrumbs, editHref, deleteSuccessNavigateHref, testIdPrefix }: UserViewProps): React.ReactElement => {
  usePageTitle(pageTitle);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userId: actorId } = useAuthProvider();
  const [confirm, setConfirm] = useState<Confirm>(null);

  const { data, isLoading } = useQuery({
    queryKey: organisationUserKey(organisationId, userId),
    queryFn: () => getOrganisationUser(organisationId, userId),
    enabled: !!organisationId && !!userId,
  });
  const user = data?.data;

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: organisationUsersListKey(organisationId) });
    queryClient.invalidateQueries({ queryKey: organisationUserKey(organisationId, userId) });
  };

  const deactivateM = useMutation({
    mutationFn: () => deactivateOrganisationUser(organisationId, userId),
    onSuccess: () => {
      invalidate();
      setConfirm(null);
    },
  });
  const reactivateM = useMutation({
    mutationFn: () => reactivateOrganisationUser(organisationId, userId),
    onSuccess: () => {
      invalidate();
      setConfirm(null);
    },
  });
  const deleteM = useMutation({
    mutationFn: () => deleteOrganisationUser(organisationId, userId),
    onSuccess: () => {
      invalidate();
      setConfirm(null);
      navigate(deleteSuccessNavigateHref);
    },
  });

  const isSelf = actorId !== null && user?.id === actorId;
  const isActive = user?.status === 'active';

  const actionItems = [
    ...(isActive && !isSelf
      ? [
          {
            label: 'Deactivate account',
            onClick: () => setConfirm('deactivate' as const),
            testId: `${testIdPrefix}-view-action-deactivate`,
          },
        ]
      : []),
    ...(!isActive
      ? [
          {
            label: 'Reactivate account',
            onClick: () => setConfirm('reactivate' as const),
            testId: `${testIdPrefix}-view-action-reactivate`,
          },
        ]
      : []),
    ...(!isSelf
      ? [
          {
            label: 'Delete account',
            onClick: () => setConfirm('delete' as const),
            testId: `${testIdPrefix}-view-action-delete`,
          },
        ]
      : []),
  ];

  return (
    <ManagementLayout breadcrumbs={breadcrumbs}>
      <div className="organisation-user-view-page">
        {isLoading || !user ? (
          <PageLoading />
        ) : (
          <>
            <div className="organisation-user-view-page__header">
              <H1 className="organisation-user-view-page__title">
                {user.firstName} {user.lastName}
              </H1>
              {actionItems.length > 0 && <GovUKActionMenu buttonLabel="Actions" items={actionItems} testId={`${testIdPrefix}-view-actions`} />}
            </div>

            <GovUKSummaryCard
              title="User details"
              actions={[
                {
                  label: 'Edit',
                  onClick: () => navigate(editHref),
                  visuallyHiddenText: 'user details',
                  testId: `${testIdPrefix}-view-edit`,
                },
              ]}
              testId={`${testIdPrefix}-view-details-card`}>
              <SummaryList
                testId={`${testIdPrefix}-view-details`}
                items={[
                  { label: 'First name', value: user.firstName },
                  { label: 'Last name', value: user.lastName },
                  { label: 'Access level', value: ROLE_LABELS[user.role] ?? user.role },
                  {
                    label: 'Email address',
                    value: (
                      <a href={`mailto:${user.email}`} className="govuk-link" data-testid={`${testIdPrefix}-view-email-link`}>
                        {user.email}
                      </a>
                    ),
                  },
                ]}
              />
            </GovUKSummaryCard>

            <GovUKSummaryCard title="Account access" testId={`${testIdPrefix}-view-security-card`}>
              <SummaryList
                testId={`${testIdPrefix}-view-security`}
                items={[
                  { label: 'Account status', value: isActive ? 'Active' : 'Inactive' },
                  {
                    label: 'MFA status',
                    value: user.mfaStatus === 'enabled' ? 'Enabled' : 'Not set up yet',
                  },
                ]}
              />
            </GovUKSummaryCard>

            <GovUKSummaryCard title="Record information" testId={`${testIdPrefix}-view-history-card`}>
              <SummaryList
                testId={`${testIdPrefix}-view-history`}
                items={[
                  {
                    label: 'Created',
                    value: user.createdBy ? (
                      <>
                        {user.createdBy.actorName}
                        <br />
                        <span className="organisation-user-view-page__history-date">{formatHistoryDate(user.createdBy.occurredAt)}</span>
                      </>
                    ) : (
                      '-'
                    ),
                  },
                  {
                    label: 'Last changed',
                    value: user.lastUpdatedBy ? (
                      <>
                        {user.lastUpdatedBy.actorName}
                        <br />
                        <span className="organisation-user-view-page__history-date">{formatHistoryDate(user.lastUpdatedBy.occurredAt)}</span>
                      </>
                    ) : (
                      '-'
                    ),
                  },
                ]}
              />
            </GovUKSummaryCard>

            {isSelf && <p className="govuk-hint organisation-user-view-page__self-hint">You cannot deactivate or delete your own account.</p>}
          </>
        )}

        {confirm === 'deactivate' && (
          <DeleteConfirmDialog
            testId={`${testIdPrefix}-deactivate-dialog`}
            open
            title="Deactivate this user?"
            message="They will be signed out immediately and unable to sign in until reactivated."
            confirmLabel="Deactivate"
            onCancel={() => setConfirm(null)}
            onConfirm={() => deactivateM.mutate()}
            isLoading={deactivateM.isPending}
          />
        )}
        {confirm === 'reactivate' && (
          <DeleteConfirmDialog
            testId={`${testIdPrefix}-reactivate-dialog`}
            open
            title="Reactivate this user?"
            message="The user will be able to sign in again. They will be prompted to re-verify MFA."
            confirmLabel="Reactivate"
            onCancel={() => setConfirm(null)}
            onConfirm={() => reactivateM.mutate()}
            isLoading={reactivateM.isPending}
          />
        )}
        {confirm === 'delete' && (
          <DeleteConfirmDialog
            testId={`${testIdPrefix}-delete-dialog`}
            open
            title="Delete this user?"
            message="This permanently removes the user. This action cannot be undone."
            confirmLabel="Delete"
            onCancel={() => setConfirm(null)}
            onConfirm={() => deleteM.mutate()}
            isLoading={deleteM.isPending}
          />
        )}
      </div>
    </ManagementLayout>
  );
};

export default UserView;
