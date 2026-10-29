import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { GeneralLayout } from '../../../layouts';
import { ErrorSummary, GovUKButton, GovUKInsetText, GovUKNotificationBanner, GovUKWarningText, H1, H2, H3, Paragraph } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { activitiesListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { canManageActivities } from '../navigation';
import { withReportContext } from '../reportContext';
import { getActivityList, type ActivitySummaryDto } from '../queries';
import { ACTIVITY_BANNER_COPY, activityTypeDisplayName, type ActivityBannerKey } from '../display';
import './styles.css';

interface BannerState {
  banner?: ActivityBannerKey;
  name?: string;
}

const STATUS_GROUPS: { key: string; label: string; action: 'change' | 'view' }[] = [
  { key: 'planned', label: 'Planned', action: 'change' },
  { key: 'active', label: 'Active', action: 'change' },
  { key: 'closed', label: 'Closed', action: 'view' },
  { key: 'finished', label: 'Finished', action: 'view' },
];

const ActivityRow = ({
  activity,
  action,
  deletable,
  decorate,
}: {
  activity: ActivitySummaryDto;
  action: 'change' | 'view' | 'continue';
  deletable: boolean;
  decorate: (path: string) => string;
}): React.ReactElement => {
  const actionLabel = action === 'change' ? 'Change' : action === 'view' ? 'View' : 'Continue setup';
  const actionHref = action === 'view' ? ROUTES.activities.detail(activity.id) : ROUTES.activities.edit(activity.id);
  const displayTitle = activity.taskName && activity.title === activity.taskName ? activityTypeDisplayName(activity.taskName) : activity.title;
  const isDraft = action === 'continue';
  return (
    <div
      style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', padding: '10px 0', borderBottom: '1px solid #b1b4b6' }}
      data-testid={`activities-row-${activity.id}`}>
      {isDraft ? (
        <span className="activities-list-page__draft-name" data-testid={`activities-row-${activity.id}-name`}>
          {displayTitle}
        </span>
      ) : (
        <RouterLink to={decorate(ROUTES.activities.detail(activity.id))} className="govuk-link govuk-!-font-weight-bold" data-testid={`activities-row-${activity.id}-name`}>
          {displayTitle}
        </RouterLink>
      )}
      <span>
        <RouterLink to={decorate(actionHref)} className="govuk-link" data-testid={`activities-row-${activity.id}-action`}>
          {actionLabel}
          <span className="govuk-visually-hidden"> {displayTitle}</span>
        </RouterLink>
        {deletable && (
          <>
            <span className="govuk-!-margin-left-2 govuk-!-margin-right-2" aria-hidden="true">
              |
            </span>
            <RouterLink
              to={decorate(ROUTES.activities.delete(activity.id))}
              state={{ name: displayTitle }}
              className="govuk-link"
              data-testid={`activities-row-${activity.id}-delete`}>
              Delete
              <span className="govuk-visually-hidden"> {displayTitle}</span>
            </RouterLink>
          </>
        )}
      </span>
    </div>
  );
};

interface Props {
  // 'report' embeds this list as the quarterly report's "confirm your activities" step (BLUM-6611):
  // it shares the standard content and adds the report-specific layout, copy and confirmation controls.
  mode?: 'standalone' | 'report';
  // The collection being reported on, in report mode. Every link out of this list carries it, so the activity page
  // the user lands on keeps the return-flow breadcrumbs and comes back here rather than to the standalone list
  // (BLUM-6967). Passed in rather than read from the URL: on Section 1 the id is a route param, not a query param.
  reportCollectionId?: string;
  backLink?: { href: string; testId?: string };
  onConfirm?: () => void;
  confirmLoading?: boolean;
  confirmError?: string | null;
  confirmAttempts?: number;
}

const ActivitiesList = ({ mode = 'standalone', reportCollectionId, backLink, onConfirm, confirmLoading = false, confirmError, confirmAttempts = 0 }: Props): React.ReactElement => {
  usePageTitle('Manage your list of activities');
  const { userRole } = useAuthProvider();
  const canManage = canManageActivities(userRole);
  const location = useLocation();
  const bannerState = (location.state as BannerState | null) ?? {};
  const banner = bannerState.banner ? ACTIVITY_BANNER_COPY[bannerState.banner] : undefined;

  // Undefined outside the report flow, so every link below is left exactly as it was.
  const decorate = (path: string) => withReportContext(path, reportCollectionId);

  const { data, isLoading } = useQuery({ queryKey: activitiesListKey, queryFn: () => getActivityList() });
  const activities = data?.data ?? [];

  const drafts = activities.filter((activity) => activity.isDraft);
  const groupFor = (key: string) => activities.filter((activity) => !activity.isDraft && activity.status === key);
  const hasAny = activities.length > 0;

  return (
    <GeneralLayout backLink={backLink ?? { href: '/' }}>
      <div className={`activities-list-page${mode === 'report' ? ' activities-list-page--report' : ''}`}>
        <ErrorSummary
          heading="There is a problem"
          errors={confirmError ? [{ targetName: 'reports-confirm-activities-submit', text: confirmError }] : []}
          focusTrigger={confirmAttempts}
        />

        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="activities-list-banner">
            <p className="govuk-body">{banner.body(bannerState.name ?? 'activity')}</p>
          </GovUKNotificationBanner>
        )}

        <div className="activities-list-page__intro">
          <H1>Manage your list of activities</H1>
          <Paragraph>Use this section to list your local authority’s gambling harms prevention work.</Paragraph>
          <Paragraph>
            You must enter all activities, whether they are funded by the gambling levy or other sources. This includes activities from commissioning providers to hiring a member
            of staff.
          </Paragraph>

          <H2>How it works</H2>
          <Paragraph>You can use your list of activities to:</Paragraph>
          <ul className="govuk-list govuk-list--bullet">
            <li>Add details of each activity and select its status (planned, active, closed or finished)</li>
            <li>Create an activity at any time and save your progress as you go</li>
            <li>Report quarterly on activities that are active, closed or finished</li>
          </ul>
          <Paragraph>If you contribute funding or resources to a joint project, you can record it as a shared activity.</Paragraph>
        </div>

        <section className="activities-list-page__activities" aria-labelledby="activities-list-heading">
          <div className="activities-list-page__heading-row">
            <H2 id="activities-list-heading">Your activities</H2>
            {canManage && (
              <RouterLink to={decorate(ROUTES.activities.create)} className="govuk-button govuk-button--secondary" data-testid="activities-create-button">
                Create activity
              </RouterLink>
            )}
          </div>

          {isLoading ? (
            <PageLoading />
          ) : !hasAny ? (
            <GovUKInsetText data-testid="activities-empty">
              {mode === 'report'
                ? 'You have not created any activities. Once you have set one up, it will appear here. You can then report on its performance.'
                : 'You have not created any activities yet. You need to create at least one activity before you can start your quarterly report.'}
            </GovUKInsetText>
          ) : (
            <>
              {STATUS_GROUPS.map((group) => {
                const rows = groupFor(group.key);
                if (rows.length === 0) {
                  return null;
                }
                return (
                  <div key={group.key} className="govuk-!-margin-bottom-6">
                    <H3>{group.label}</H3>
                    {rows.map((activity) => (
                      <ActivityRow key={activity.id} activity={activity} action={group.action} deletable={group.key === 'planned'} decorate={decorate} />
                    ))}
                  </div>
                );
              })}

              {drafts.length > 0 && (
                <div className="govuk-!-margin-bottom-6">
                  <H3>Drafts</H3>
                  {drafts.map((activity) => (
                    <ActivityRow key={activity.id} activity={activity} action="continue" deletable decorate={decorate} />
                  ))}
                </div>
              )}
            </>
          )}

          <p className="govuk-body activities-list-page__deleted-link">
            <RouterLink to={decorate(ROUTES.activities.deleted)} className="govuk-link" data-testid="activities-view-deleted-link">
              View deleted activities
            </RouterLink>
          </p>
        </section>

        <div className="activities-list-page__reporting-deadlines">
          <H2>Reporting deadlines</H2>
          <Paragraph>You must provide a progress update on your activities each quarter. We’ll email you when your next report is due.</Paragraph>
        </div>

        {mode === 'report' && (
          <>
            <section className="activities-list-page__confirm" aria-labelledby="activities-confirm-heading">
              <H2 id="activities-confirm-heading">Confirm your activities for this quarter</H2>
              <div className="activities-list-page__confirm-copy">
                <Paragraph>Your quarterly report only asks for updates on activities marked as active, closed or finished.</Paragraph>
                <Paragraph>
                  Check your list to make sure the status of each activity is correct for this quarter. For example, change an activity from ‘planned’ to ‘active’ if work has now
                  started.
                </Paragraph>
              </div>
              <GovUKWarningText>
                If you change your activity list after starting your quarterly report, you’ll need to review and reconfirm each section of the report.
              </GovUKWarningText>
              {confirmError && (
                <p className="govuk-error-message" data-testid="reports-confirm-activities-error">
                  <span className="govuk-visually-hidden">Error:</span> {confirmError}
                </p>
              )}
            </section>
            {canManage && (
              <div className="activities-list-page__confirm-action">
                <GovUKButton onClick={onConfirm} isLoading={confirmLoading} name="reports-confirm-activities-submit" data-testid="reports-confirm-activities-submit">
                  Confirm and continue
                </GovUKButton>
              </div>
            )}
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default ActivitiesList;
