import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { useParams, useNavigate, Link as RouterLink } from 'react-router-dom';
import { useQuery } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import { GovUKSummaryCard, isQuestionVisible, SummaryList, H1, type SummaryCardAction } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle, { pageTitleWithName } from '../../../hooks/usePageTitle';
import { activityKey, activityTaxonomyKey, defaultStaleTime } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { canManageActivities } from '../navigation';
import { useActivityReportContext } from '../reportContext';
import { getActivity, getActivityFormSchema, getActivityTaxonomy } from '../queries';
import { resolveAnswerDisplay } from '../answers';
import {
  ACTIVITY_NAME_QUESTION,
  ADDITIONAL_INFORMATION_SECTION,
  activityCopy,
  CATEGORY_QUESTION,
  expectedResultRowsDisplay,
  formatStartDate,
  isActivitySectionApplicable,
  isPeopleCategory,
  keyResultsForTask,
  keyResultsQuestionLabel,
  renderAnswerDisplay,
  sectionShowsNameField,
  statusLabel,
} from '../display';
import '../../../components/Global/Activities/ActivityWizard/styles.css';

const ActivitiesView = (): React.ReactElement => {
  const { activityId = '' } = useParams<{ activityId: string }>();
  const navigate = useNavigate();
  const { userRole } = useAuthProvider();
  const canManage = canManageActivities(userRole);
  // Set when the activity was opened from Section 1 of a quarterly return (BLUM-6967).
  const report = useActivityReportContext();

  const { data, isLoading } = useQuery(activityKey(activityId), () => getActivity(activityId), { enabled: !!activityId });
  const activity = data?.data;

  // Named rather than a bare 'Activity' so the tab and the page-change announcement say WHICH activity, matching the
  // edit route's "Activity - {title}" (WCAG 2.2 SC 2.4.2). Set after the read, so it needs the record in hand.
  usePageTitle(pageTitleWithName('Activity', activity?.title));

  const schemaQuery = useQuery(['activity-form-schema', activity?.categoryId], () => getActivityFormSchema(activity?.categoryId ?? undefined), {
    enabled: !!activity?.categoryId,
    staleTime: defaultStaleTime,
  });
  const schema = schemaQuery.data?.data;

  // Resolves standard key-result codes to their live taxonomy labels (snapshotted text is the fallback).
  const taxonomyQuery = useQuery(activityTaxonomyKey, getActivityTaxonomy, { staleTime: defaultStaleTime });
  const taxonomy = taxonomyQuery.data?.data;

  if (isLoading || !activity) {
    return (
      <GeneralLayout breadcrumbs={report.breadcrumbs(userRole)} backLink={{ href: report.listPath, testId: 'activities-view-back' }}>
        <div style={{ paddingTop: '20px' }}>
          <PageLoading />
        </div>
      </GeneralLayout>
    );
  }

  const isPeople = isPeopleCategory(activity.categoryName);
  const copy = activityCopy(isPeople, activity.categoryName);
  // Completed Planned/Active records can be changed section-by-section. Drafts use "Continue setup"; Closed and
  // Finished records are fully read-only (no Change links anywhere).
  const sectionEditable = canManage && !activity.isDraft && (activity.status === 'planned' || activity.status === 'active');
  const readOnlyCompleted = !activity.isDraft && (activity.status === 'closed' || activity.status === 'finished');
  // Figma 420:28647 uses the same summary-card layout as the editable completed view, but without actions.
  const usesCompletedSummaryLayout = sectionEditable || readOnlyCompleted;

  const changeAction = (target: string, hidden: string): SummaryCardAction[] =>
    sectionEditable ? [{ label: 'Change', onClick: () => navigate(report.activityLink(`${ROUTES.activities.edit(activityId)}?edit=${target}`)), visuallyHiddenText: hidden }] : [];

  // Keyed by question code, matching the shape isQuestionVisible and the wizard's form state expect.
  const answerMap = activity.answers.reduce<Record<string, string>>((acc, answer) => {
    if (answer.value) {
      acc[answer.questionCode] = answer.value;
    }
    return acc;
  }, {});
  const answerValue = (code: string) => answerMap[code];
  // Every question the record was asked is listed, answered or not — an unanswered one renders "Not provided"
  // rather than vanishing (BLUM-7187: activities predating a schema addition, e.g. Education/Help gaining the
  // shared Activity details questions in BLUM-7120, silently showed an almost-empty card). Section applicability
  // and conditional visibility are the same predicates the wizard, editor and check-answers use, so the four
  // surfaces agree on which questions exist for a record.
  const responseSections = (schema?.sections ?? [])
    .filter((section) => section.isActive)
    .filter((section) => isActivitySectionApplicable(section.title, activity.categoryName))
    .filter((section) => !section.activityTaskId || section.activityTaskId === activity.taskId)
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .map((section) => ({
      section,
      questions: (schema?.questions ?? [])
        .filter((question) => question.sectionId === section.id && question.isActive && isQuestionVisible(question, answerMap))
        .sort((a, b) => a.displayOrder - b.displayOrder),
    }))
    .filter((group) => group.questions.length > 0 || (usesCompletedSummaryLayout && sectionShowsNameField(group.section.title, isPeople)));

  const detailSections = responseSections.filter(({ section }) => section.title !== ADDITIONAL_INFORMATION_SECTION);
  const additionalInformationSections = responseSections.filter(({ section }) => section.title === ADDITIONAL_INFORMATION_SECTION);
  const inlineAnswer = (values: string[]): string => (values.length > 0 ? values.join(', ') : 'Not provided');

  const standardExpectedResults = activity.expectedResults
    .filter((row) => row.keyResultCode != null)
    .map((row) => taxonomy?.keyResults.find((keyResult) => keyResult.code === row.keyResultCode)?.label ?? row.text)
    .filter(Boolean);
  const customExpectedResults = activity.expectedResults
    .filter((row) => row.keyResultCode == null)
    .map((row) => row.text.trim())
    .filter(Boolean);
  const hasStandardKeyResultOptions = !taxonomy || keyResultsForTask(taxonomy, activity.taskId ?? '').length > 0;
  const completedExpectedResultItems = hasStandardKeyResultOptions
    ? [
        {
          label: keyResultsQuestionLabel(true),
          value: inlineAnswer([...standardExpectedResults, ...(customExpectedResults.length > 0 ? ['Other'] : [])]),
        },
        ...(customExpectedResults.length > 0 ? [{ label: 'What other result will you use?', value: inlineAnswer(customExpectedResults) }] : []),
      ]
    : customExpectedResults.map((value, index) => ({ label: `Key result ${index + 1}`, value }));

  const statusItems = [{ label: copy.statusQuestion, value: statusLabel(activity.status, activity.isDraft) }];
  const startDisplay = formatStartDate(activity.startMonth, activity.startYear);
  if (activity.status === 'active' && startDisplay) {
    statusItems.push({ label: copy.startQuestion, value: startDisplay });
  }

  const renderResponseSection = ({ section, questions }: (typeof responseSections)[number]): React.ReactNode => {
    const nameItem =
      usesCompletedSummaryLayout && sectionShowsNameField(section.title, isPeople) ? [{ label: ACTIVITY_NAME_QUESTION, value: activity.name?.trim() || 'Not provided' }] : [];
    // Cards keep their real schema section title — Education/Help have two detail sections (Activity details
    // and People and issues, BLUM-7120), so the completed layout's old blanket retitle would render duplicates.
    const sectionTitle = section.title;

    return (
      <GovUKSummaryCard key={section.id} title={sectionTitle} testId={`activities-view-section-${section.id}`} actions={changeAction(section.id, sectionTitle)}>
        <SummaryList
          halfWidthColumns={usesCompletedSummaryLayout}
          testId={`activities-view-section-${section.id}-answers`}
          items={[
            ...nameItem,
            ...questions.map((question) => ({
              label: question.label,
              value: usesCompletedSummaryLayout
                ? inlineAnswer(resolveAnswerDisplay(question, answerValue(question.code)))
                : renderAnswerDisplay(resolveAnswerDisplay(question, answerValue(question.code))),
            })),
          ]}
        />
      </GovUKSummaryCard>
    );
  };

  const expectedResultsCard = activity.expectedResults.length > 0 && (
    <GovUKSummaryCard title="Expected results" testId="activities-view-expected-results" actions={changeAction('expected-results', 'expected results')}>
      <SummaryList
        halfWidthColumns={usesCompletedSummaryLayout}
        items={
          usesCompletedSummaryLayout
            ? completedExpectedResultItems
            : [
                {
                  label: keyResultsQuestionLabel(hasStandardKeyResultOptions),
                  value: renderAnswerDisplay(expectedResultRowsDisplay(activity.expectedResults, taxonomy)),
                },
              ]
        }
      />
    </GovUKSummaryCard>
  );

  const statusCard = (
    <GovUKSummaryCard title="Activity status" testId="activities-view-status" actions={changeAction('status', 'activity status')}>
      <SummaryList halfWidthColumns={usesCompletedSummaryLayout} items={statusItems} />
    </GovUKSummaryCard>
  );

  return (
    <GeneralLayout
      breadcrumbs={usesCompletedSummaryLayout ? (report.inReportFlow ? report.breadcrumbs(userRole) : undefined) : report.breadcrumbs(userRole)}
      // The Back link renders in the layout chrome, above #main-content, so "Skip to main content" bypasses it
      // instead of landing on it (BLUM-7256). The completed-summary layout keeps its own 25/50 spacing.
      backLink={{ href: report.listPath, testId: 'activities-view-back', className: usesCompletedSummaryLayout ? 'activity-wizard__back-link' : undefined }}>
      <div className={usesCompletedSummaryLayout ? 'activity-wizard activity-wizard--check' : undefined} style={usesCompletedSummaryLayout ? undefined : { paddingTop: '20px' }}>
        <div className={usesCompletedSummaryLayout ? 'activity-wizard__title-block' : undefined}>
          <span className="govuk-caption-l">Activity</span>
          <H1 className={usesCompletedSummaryLayout ? 'activity-wizard__heading' : undefined}>{activity.title}</H1>
        </div>

        {/* Category and type are locked once setup is complete — never a Change link here. */}
        <GovUKSummaryCard title="Activity category" testId="activities-view-category">
          <SummaryList halfWidthColumns={usesCompletedSummaryLayout} items={[{ label: CATEGORY_QUESTION, value: activity.categoryName ?? 'Not provided' }]} />
        </GovUKSummaryCard>

        <GovUKSummaryCard title={copy.typeTitle} testId="activities-view-type">
          <SummaryList
            halfWidthColumns={usesCompletedSummaryLayout}
            items={[{ label: copy.typeQuestion, value: activity.taskName ?? activity.customTaskText ?? 'Not provided' }]}
          />
        </GovUKSummaryCard>

        {usesCompletedSummaryLayout ? (
          <>
            {detailSections.map(renderResponseSection)}
            {expectedResultsCard}
            {additionalInformationSections.map(renderResponseSection)}
            {statusCard}
          </>
        ) : (
          <>
            {responseSections.map(renderResponseSection)}
            {expectedResultsCard}
            {statusCard}
          </>
        )}

        {canManage && activity.isDraft && (
          <div className="govuk-button-group">
            <RouterLink to={report.activityLink(ROUTES.activities.edit(activityId))} className="govuk-button" data-testid="activities-view-continue-setup">
              Continue setup
            </RouterLink>
          </div>
        )}

        <p className="govuk-body">
          <RouterLink to={report.listPath} className="govuk-link" data-testid="activities-view-return">
            Return to your activities
          </RouterLink>
        </p>
      </div>
    </GeneralLayout>
  );
};

export default ActivitiesView;
