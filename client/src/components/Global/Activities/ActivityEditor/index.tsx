import React, { useEffect, useLayoutEffect, useMemo, useReducer, useRef, useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, H1 } from '../../../GovUKComponents';
import StepDynamicSection from '../StepDynamicSection';
import StepExpectedResults from '../StepExpectedResults';
import StepStatus from '../StepStatus';
import StepCheckAnswers from '../StepCheckAnswers';
import ConfirmationPanel from '../ConfirmationPanel';
import { useAuthProvider } from '../../../AuthProvider';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import usePageTitle from '../../../../hooks/usePageTitle';
import scrollToTop from '../../../../helpers/scrollToTop';
import { activitiesListKey, activityKey, activityTaxonomyKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import { invalidateReportCaches, useActivityReportContext } from '../../../../views/Activities/reportContext';
import { activityFormReducer, initialActivityFormState, type ActivityFormState } from '../../../../views/Activities/reducer';
import { getActivityFormSchema, getActivityTaxonomy, type ActivityDto } from '../../../../views/Activities/queries';
import { updateActivity } from '../../../../views/Activities/mutations';
import { collectAnswers, questionErrorTargetId, validateActivityAnswers } from '../../../../views/Activities/answers';
import { mapActivityErrorFields } from '../../../../views/Activities/messages';
import {
  activityCopy,
  activityTypeDisplayName,
  buildExpectedResultsPayload,
  categoryUsesExpectedResults,
  isActivitySectionApplicable,
  isNeedsAssessmentCategory,
  isPeopleCategory,
  keyResultsForTask,
  sectionShowsNameField,
  splitExpectedResults,
  validateKeyResultSelections,
  validateNameLength,
  validateStartDate,
} from '../../../../views/Activities/display';
import '../ActivityWizard/styles.css';

type EditTarget = { kind: 'section'; sectionId: string } | { kind: 'expectedResults' } | { kind: 'status' };

interface Props {
  activity: ActivityDto;
  initialEdit?: string;
}

// Edits a completed (Planned/Active) record using the GOV.UK "check your answers" pattern: change one section at
// a time, then Save changes / Discard changes (Figma 138-10605 / 138-10585). Category and type are locked.
const ActivityEditor = ({ activity, initialEdit }: Props): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  // Set when the editor was entered from Section 1 of a quarterly return (BLUM-6967).
  const report = useActivityReportContext();

  const hydrated: ActivityFormState = {
    ...initialActivityFormState,
    title: activity.name ?? '',
    categoryId: activity.categoryId ?? '',
    taskId: activity.taskId ?? '',
    customTaskText: activity.customTaskText ?? '',
    answers: activity.answers.reduce<Record<string, string>>((acc, answer) => {
      if (answer.value != null) {
        acc[answer.questionCode] = answer.value;
      }
      return acc;
    }, {}),
    ...splitExpectedResults(activity.expectedResults),
    status: activity.status ?? '',
    startMonth: activity.startMonth != null ? String(activity.startMonth) : '',
    startYear: activity.startYear != null ? String(activity.startYear) : '',
  };

  const [state, dispatch] = useReducer(activityFormReducer, hydrated);

  const taxonomyQuery = useQuery(activityTaxonomyKey, getActivityTaxonomy, { staleTime: defaultStaleTime });
  const taxonomy = taxonomyQuery.data?.data;
  const schemaQuery = useQuery(['activity-form-schema', activity.categoryId], () => getActivityFormSchema(activity.categoryId ?? undefined), {
    enabled: !!activity.categoryId,
    staleTime: defaultStaleTime,
  });

  const isPeople = isPeopleCategory(activity.categoryName);
  const isNeedsAssessment = isNeedsAssessmentCategory(activity.categoryName);
  const usesExpectedResults = categoryUsesExpectedResults(activity.categoryName);
  const copy = activityCopy(isPeople, activity.categoryName);
  const selectedTypeName = activity.customTaskText ?? (activity.taskName ? activityTypeDisplayName(activity.taskName) : undefined);

  // Sections scoped to a specific activity type only apply to activities of that type (the editor's taxonomy
  // is locked, so this is a fixed filter).
  const sections = useMemo(
    () =>
      (schemaQuery.data?.data.sections ?? [])
        .filter((section) => section.isActive)
        .filter((section) => isActivitySectionApplicable(section.title, activity.categoryName))
        .filter((section) => !section.activityTaskId || section.activityTaskId === activity.taskId)
        .sort((a, b) => a.displayOrder - b.displayOrder),
    [schemaQuery.data, activity.taskId, activity.categoryName]
  );
  const allQuestions = useMemo(
    () =>
      (schemaQuery.data?.data.questions ?? [])
        .filter((question) => question.isActive)
        .filter((question) => sections.some((section) => section.id === question.sectionId))
        .sort((a, b) => a.displayOrder - b.displayOrder),
    [schemaQuery.data, sections]
  );
  const sectionQuestions = (sectionId: string) => allQuestions.filter((question) => question.sectionId === sectionId);
  // The name field is part of the "Activity details" step body, so the step that renders it is the step that validates it.
  const sectionShowsName = (sectionId: string | undefined) => sectionShowsNameField(sections.find((section) => section.id === sectionId)?.title, isPeople);
  // Treat the type as having standard key-result options while the taxonomy is still loading so hydrated
  // selections are never discounted.
  const hasStandardKeyResults = !taxonomy || keyResultsForTask(taxonomy, state.taskId).length > 0;

  const editTargets = useMemo<EditTarget[]>(() => {
    const targets: EditTarget[] = sections.map((section) => ({ kind: 'section', sectionId: section.id }));
    if (usesExpectedResults) {
      targets.push({ kind: 'expectedResults' });
    }
    targets.push({ kind: 'status' });
    return targets;
  }, [sections, usesExpectedResults]);

  const resolveInitial = (): EditTarget | 'check' => {
    if (!initialEdit) {
      return 'check';
    }
    if (initialEdit === 'status') {
      return { kind: 'status' };
    }
    if (initialEdit === 'expected-results' || initialEdit === 'expectedResults') {
      return usesExpectedResults ? { kind: 'expectedResults' } : 'check';
    }
    return sections.some((section) => section.id === initialEdit) ? { kind: 'section', sectionId: initialEdit } : 'check';
  };

  const [mode, setMode] = useState<EditTarget | 'check'>(resolveInitial);
  const [discarding, setDiscarding] = useState(false);
  const [hasPendingChanges, setHasPendingChanges] = useState(false);

  // The `?edit=` deep-link target depends on the (async) schema sections. If the schema isn't cached at mount
  // (e.g. a direct load/refresh on the edit URL), resolveInitial() in useState defaults to 'check'; re-resolve
  // once the sections load, applying the requested target a single time.
  const initialApplied = useRef(false);
  useEffect(() => {
    if (initialApplied.current || sections.length === 0) {
      return;
    }
    initialApplied.current = true;
    const target = resolveInitial();
    if (target !== 'check') {
      setMode(target);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sections]);

  const headingFor = (target: EditTarget): string => {
    if (target.kind === 'section') {
      return sections.find((section) => section.id === target.sectionId)?.title ?? 'Activity details';
    }
    return target.kind === 'expectedResults' ? 'Expected results' : copy.statusTitle;
  };
  // In check mode the visible H1 is the activity's own title (not "Check your answers"), so mirror it here to
  // keep the document title consistent with the heading a screen-reader user lands on.
  const currentPageTitle = discarding ? 'Discard changes' : mode === 'check' ? activity.title : headingFor(mode);
  const hasErrors = Object.keys(state.errors).length > 0;

  usePageTitle(`${hasErrors && !discarding ? 'Error: ' : ''}${currentPageTitle}`);
  useErrorSummaryFocus(state.submitAttempts, hasErrors);

  // Editing a section, returning to check answers, and entering/leaving the discard interruption are all
  // in-page view changes. Scroll to the top and move focus to the new heading so keyboard and screen-reader
  // users encounter the replacement content from the top. While discarding, the heading is unmounted (the
  // ConfirmationPanel is shown and self-focuses its own heading), so getElementById no-ops here; on cancel
  // the heading is back and regains focus.
  useLayoutEffect(() => {
    scrollToTop();
    document.getElementById('activity-editor-heading')?.focus({ preventScroll: true });
  }, [mode, discarding]);

  const validateTarget = (target: EditTarget): Record<string, string> => {
    switch (target.kind) {
      case 'section': {
        const errors = validateActivityAnswers(sectionQuestions(target.sectionId), state.answers);
        const nameError = sectionShowsName(target.sectionId) ? validateNameLength(state.title, 'Activity name') : null;
        if (nameError) {
          errors['activity-name'] = nameError;
        }
        return errors;
      }
      case 'expectedResults':
        return validateKeyResultSelections(state, hasStandardKeyResults, isNeedsAssessment, !!taxonomy);
      default: {
        const errors: Record<string, string> = {};
        if (!state.status) {
          errors['activity-status'] = copy.statusError;
        } else {
          const startError = validateStartDate(state.status, state.startMonth, state.startYear);
          if (startError) {
            errors[startError.field] = startError.message;
          }
        }
        return errors;
      }
    }
  };

  const buildPayload = () => ({
    id: activity.id,
    // People-and-roles activities never render the name field, so any stored name is unreachable — don't write
    // one back. isPeople comes from the loaded activity here, so there is no taxonomy-loading window to guard.
    title: isPeople ? null : state.title.trim() || null,
    categoryId: state.categoryId || null,
    taskId: state.taskId || null,
    customTaskText: state.customTaskText.trim() || null,
    status: state.status || null,
    startMonth: state.startMonth ? Number(state.startMonth) : null,
    startYear: state.startYear ? Number(state.startYear) : null,
    expectedResults: buildExpectedResultsPayload(state),
    answers: collectAnswers(allQuestions, state.answers),
    saveAsDraft: false,
  });

  const updateMutation = useMutation(updateActivity, {
    onSuccess: () => {
      queryClient.invalidateQueries(activitiesListKey);
      queryClient.invalidateQueries(activityKey(activity.id));
      invalidateReportCaches(queryClient, report.collectionId);
      navigate(report.listPath, { state: { banner: 'updated', name: activity.title } });
    },
    onError: (error) => dispatch({ type: 'SET_ERRORS', errors: mapActivityErrorFields(error) }),
  });

  const continueFromSection = (target: EditTarget) => {
    const errors = validateTarget(target);
    dispatch({ type: 'BUMP_ATTEMPTS' });
    dispatch({ type: 'SET_ERRORS', errors });
    if (Object.keys(errors).length === 0) {
      setHasPendingChanges(true);
      setMode('check');
    }
  };

  const handleSave = () => {
    // Validate every section before committing; jump to the first one with a problem.
    for (const target of editTargets) {
      const errors = validateTarget(target);
      if (Object.keys(errors).length > 0) {
        dispatch({ type: 'BUMP_ATTEMPTS' });
        dispatch({ type: 'SET_ERRORS', errors });
        setMode(target);
        return;
      }
    }
    dispatch({ type: 'SET_ERRORS', errors: {} });
    updateMutation.mutate(buildPayload());
  };

  const errorList = Object.entries(state.errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => {
      const question = allQuestions.find((item) => item.code === key);
      return { targetName: question ? questionErrorTargetId(question) : key.toLowerCase(), text: message as string };
    });

  const answerSections = sections.map((section) => ({
    section,
    questions: sectionQuestions(section.id),
    stepIndex: editTargets.findIndex((target) => target.kind === 'section' && target.sectionId === section.id),
    showNameField: sectionShowsName(section.id),
  }));

  const renderTarget = (target: EditTarget): React.ReactNode => {
    if (target.kind === 'section') {
      const section = sections.find((item) => item.id === target.sectionId)!;
      return (
        <StepDynamicSection
          section={section}
          questions={sectionQuestions(section.id)}
          state={state}
          dispatch={dispatch}
          showNameField={sectionShowsName(section.id)}
          selectedTypeName={selectedTypeName}
        />
      );
    }
    if (target.kind === 'expectedResults') {
      return <StepExpectedResults state={state} dispatch={dispatch} keyResults={taxonomy?.keyResults ?? []} isNeedsAssessment={isNeedsAssessment} />;
    }
    return <StepStatus state={state} dispatch={dispatch} isPeople={isPeople} />;
  };

  // The Back link renders in the layout chrome, above #main-content, so "Skip to main content" bypasses it instead
  // of landing on it (BLUM-7256). The design (Figma 420-29290) keeps it visible above the discard interruption card
  // too; going back from there is the same non-destructive exit as "Go back to activity".
  const goBack = () => {
    if (discarding) {
      setDiscarding(false);
      return;
    }
    if (mode === 'check') {
      navigate(report.listPath);
      return;
    }
    setMode('check');
  };

  return (
    <GeneralLayout
      breadcrumbs={report.inReportFlow ? report.breadcrumbs(userRole, [{ label: 'Change activity', link: '#' }]) : undefined}
      backLink={{
        onClick: goBack,
        testId: 'activities-edit-back',
        // Check mode reproduces the wizard's 25/50 spacing; section editing reproduces the 20px wrapper padding
        // plus govuk-frontend's default 15px top margin that the link used to sit under.
        className: mode === 'check' ? 'activity-wizard__back-link' : 'activity-wizard__back-link--section',
      }}>
      <div className={mode === 'check' ? 'activity-wizard activity-wizard--check' : undefined}>
        {discarding ? (
          <>
            <ConfirmationPanel
              testId="activities-discard"
              title="Are you sure you want to discard your changes?"
              body={<>If you discard your changes, you’ll lose any new information you have added to the activity ‘{activity.title}’.</>}
              confirmLabel="Discard changes"
              onConfirm={() => navigate(report.listPath, { state: { banner: 'discarded', name: activity.title } })}
              cancelLabel="Go back to activity"
              onCancel={() => setDiscarding(false)}
            />
          </>
        ) : (
          <>
            {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

            {mode === 'check' ? (
              <>
                <div className="activity-wizard__title-block">
                  <span className="govuk-caption-l">Activity</span>
                  <H1 className="activity-wizard__heading" id="activity-editor-heading" tabIndex={-1}>
                    {activity.title}
                  </H1>
                </div>
                <StepCheckAnswers
                  state={state}
                  taxonomy={taxonomy}
                  isPeople={isPeople}
                  answerSections={answerSections}
                  categoryStepIndex={null}
                  typeStepIndex={null}
                  expectedResultsStepIndex={usesExpectedResults ? editTargets.findIndex((t) => t.kind === 'expectedResults') : null}
                  statusStepIndex={editTargets.findIndex((t) => t.kind === 'status')}
                  additionalInformationPlacement="before-status"
                  onChange={(index) => setMode(editTargets[index])}
                />
                {hasPendingChanges && (
                  <div className="govuk-button-group">
                    <GovUKButton onClick={handleSave} isLoading={updateMutation.isLoading} data-testid="activities-edit-save">
                      Save changes
                    </GovUKButton>
                    <GovUKButton className="govuk-button govuk-button--secondary" onClick={() => setDiscarding(true)} data-testid="activities-edit-discard">
                      Discard changes
                    </GovUKButton>
                  </div>
                )}
                <p className="govuk-body">
                  <RouterLink to={report.listPath} className="govuk-link" data-testid="activities-edit-return">
                    Return to your activities
                  </RouterLink>
                </p>
              </>
            ) : (
              <>
                <span className="govuk-caption-l">Change activity</span>
                <H1 id="activity-editor-heading" tabIndex={-1}>
                  {headingFor(mode)}
                </H1>
                {renderTarget(mode)}
                <div className="govuk-button-group">
                  <GovUKButton onClick={() => continueFromSection(mode)} data-testid="activities-edit-continue">
                    Continue
                  </GovUKButton>
                </div>
              </>
            )}
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default ActivityEditor;
