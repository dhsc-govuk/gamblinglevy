import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { useParams, useNavigate, Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { GovUKButton, GovUKSummaryCard, isQuestionVisible, SummaryList, H1, type SummaryCardAction } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle, { pageTitleWithName } from '../../../hooks/usePageTitle';
import { activityFormSchemaKeyFor, activityKey, activityTaxonomyKey, defaultStaleTime } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { downloadErrorMessage } from '../../../helpers/downloadErrors';
import { canManageActivities } from '../navigation';
import { useActivityReportContext } from '../reportContext';
import { getActivity, getActivityFormSchema, getActivityTaxonomy } from '../queries';
import { downloadActivityRecord } from '../mutations';
import { resolveAnswerDisplay } from '../answers';
import {
  ACTIVITY_NAME_QUESTION,
  ADDITIONAL_INFORMATION_SECTION,
  activityCopy,
  CATEGORY_QUESTION,
  EXPECTED_RESULTS_QUESTION,
  OTHER_MEASURE_LABEL,
  expectedResultRowsDisplay,
  forcedActivityStatus,
  formatStartDate,
  isActivitySectionApplicable,
  isPeopleCategory,
  keyResultsForTask,
  measureRowLabel,
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

  const { data, isLoading } = useQuery({ queryKey: activityKey(activityId), queryFn: () => getActivity(activityId), enabled: !!activityId });
  const activity = data?.data;

  // Named rather than a bare 'Activity' so the tab and the page-change announcement say WHICH activity, matching the
  // edit route's "Activity - {title}" (WCAG 2.2 SC 2.4.2). Set after the read, so it needs the record in hand.
  usePageTitle(pageTitleWithName('Activity', activity?.title));

  const schemaQuery = useQuery({
    queryKey: activityFormSchemaKeyFor(activity?.categoryId),
    queryFn: () => getActivityFormSchema(activity?.categoryId ?? undefined),
    enabled: !!activity?.categoryId,
    staleTime: defaultStaleTime,
  });
  const schema = schemaQuery.data?.data;

  // Resolves standard key-result codes to their live taxonomy labels (snapshotted text is the fallback).
  const taxonomyQuery = useQuery({ queryKey: activityTaxonomyKey, queryFn: getActivityTaxonomy, staleTime: defaultStaleTime });
  const taxonomy = taxonomyQuery.data?.data;

  // BLUM-7648. Declared above the loading return so the hook order is the same on every render.
  const downloadMutation = useMutation({ mutationFn: () => downloadActivityRecord(activityId) });

  // The taxonomy is in the gate because showTypeCard below is derived from it: ungated, a cold load renders the
  // "Activity type" card and then removes it when the query resolves (the same shape as the BLUM-7792 drift, which
  // this page still carries elsewhere).
  if (isLoading || taxonomyQuery.isLoading || !activity) {
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
  // Mirrors the wizard, the editor and check-answers (BLUM-7860): a category offering exactly one activity type
  // never asked the type question, so there is no type card; one whose status is fixed never asked for a status or
  // a start date, so its status shows read-only with no start date and no Change link (BLUM-8090).
  const forcedStatus = forcedActivityStatus(activity.categoryName);
  const showTypeCard = (taxonomy?.tasks ?? []).filter((task) => task.categoryId === activity.categoryId).length !== 1;
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
    .filter((group) => group.questions.length > 0 || (usesCompletedSummaryLayout && sectionShowsNameField(group.section.title, activity.categoryName)));

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
          label: EXPECTED_RESULTS_QUESTION,
          value: inlineAnswer([...standardExpectedResults, ...(customExpectedResults.length > 0 ? ['Other'] : [])]),
        },
        ...(customExpectedResults.length > 0 ? [{ label: OTHER_MEASURE_LABEL, value: inlineAnswer(customExpectedResults) }] : []),
      ]
    : customExpectedResults.map((value, index) => ({ label: measureRowLabel(index), value }));

  const statusItems = [{ label: copy.statusQuestion, value: statusLabel(activity.status, activity.isDraft) }];
  const startDisplay = formatStartDate(activity.startMonth, activity.startYear);
  // Not for a fixed status even when the record carries a date: one created before BLUM-8090 may, but the question
  // is no longer asked, so showing it would be an answer nobody can change.
  if (!forcedStatus && activity.status === 'active' && startDisplay) {
    statusItems.push({ label: copy.startQuestion, value: startDisplay });
  }
  // Unlike the start date, listed even when empty: a record closed before the question existed (BLUM-7673) reads
  // "Not provided" rather than hiding that no reason was recorded — the BLUM-7187 rule for unanswered questions.
  if (!forcedStatus && activity.status === 'closed') {
    statusItems.push({ label: copy.closureQuestion, value: activity.closureReason?.trim() || 'Not provided' });
  }

  const renderResponseSection = ({ section, questions }: (typeof responseSections)[number]): React.ReactNode => {
    const nameItem =
      usesCompletedSummaryLayout && sectionShowsNameField(section.title, activity.categoryName)
        ? [{ label: ACTIVITY_NAME_QUESTION, value: activity.name?.trim() || 'Not provided' }]
        : [];
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
        testId="activities-view-expected-results-answers"
        items={
          usesCompletedSummaryLayout
            ? completedExpectedResultItems
            : [
                {
                  label: EXPECTED_RESULTS_QUESTION,
                  value: renderAnswerDisplay(expectedResultRowsDisplay(activity.expectedResults, taxonomy)),
                },
              ]
        }
      />
    </GovUKSummaryCard>
  );

  const statusCard = (
    <GovUKSummaryCard title="Activity status" testId="activities-view-status" actions={forcedStatus ? [] : changeAction('status', 'activity status')}>
      <SummaryList halfWidthColumns={usesCompletedSummaryLayout} testId="activities-view-status-answers" items={statusItems} />
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

        {showTypeCard && (
          <GovUKSummaryCard title={copy.typeTitle} testId="activities-view-type">
            <SummaryList
              halfWidthColumns={usesCompletedSummaryLayout}
              items={[{ label: copy.typeQuestion, value: activity.taskName ?? activity.customTaskText ?? 'Not provided' }]}
            />
          </GovUKSummaryCard>
        )}

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

        {downloadMutation.isError && (
          // role="alert" so a failed download is announced: focus stays on the button, and without a live region a
          // screen-reader user is left waiting for a file that never arrives (WCAG 2.2 SC 4.1.3). Above
          // the buttons, as GDS places an error before its control, so it cannot read as belonging to the Return link.
          <p className="govuk-error-message" role="alert" data-testid="activities-view-download-error">
            <span className="govuk-visually-hidden">Error:</span> {downloadErrorMessage(downloadMutation.error)}
          </p>
        )}
        <div className="govuk-button-group">
          {canManage && activity.isDraft && (
            <RouterLink to={report.activityLink(ROUTES.activities.edit(activityId))} className="govuk-button" data-testid="activities-view-continue-setup">
              Continue setup
            </RouterLink>
          )}
          {/* The whole record as a CSV — every field above plus what submitted quarterly reports recorded for it
              (BLUM-7648). Secondary so it never competes with "Continue setup" on a draft. */}
          <GovUKButton
            className="govuk-button govuk-button--secondary"
            onClick={() => downloadMutation.mutate()}
            isLoading={downloadMutation.isPending}
            data-testid="activities-view-download">
            Download activity record (CSV)
          </GovUKButton>
        </div>

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
