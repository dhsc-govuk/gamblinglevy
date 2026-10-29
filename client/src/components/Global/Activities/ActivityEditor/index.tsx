import React, { useEffect, useLayoutEffect, useMemo, useReducer, useRef, useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, H1 } from '../../../GovUKComponents';
import StepDynamicSection from '../StepDynamicSection';
import StepExpectedResults from '../StepExpectedResults';
import StepStatus from '../StepStatus';
import StepCheckAnswers from '../StepCheckAnswers';
import ConfirmationPanel from '../ConfirmationPanel';
import { useAuthProvider } from '../../../AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import scrollToTop from '../../../../helpers/scrollToTop';
import { activitiesListKey, activityFormSchemaKeyFor, activityKey, activityTaxonomyKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import { invalidateReportCaches, useActivityReportContext } from '../../../../views/Activities/reportContext';
import { activityFormReducer, initialActivityFormState, type ActivityFormState } from '../../../../views/Activities/reducer';
import { getActivityFormSchema, getActivityTaxonomy, type ActivityDto } from '../../../../views/Activities/queries';
import { updateActivity } from '../../../../views/Activities/mutations';
import { collectAnswers, questionErrorTargetId, validateAnswers } from '../../../../views/Activities/answers';
import { mapActivityErrorFields } from '../../../../views/Activities/messages';
import {
  activityCopy,
  buildExpectedResultsPayload,
  categoryUsesExpectedResults,
  EXPECTED_RESULTS_ERROR_KEY,
  expectedResultsErrorTargetId,
  forcedActivityStatus,
  isActivitySectionApplicable,
  isPeopleCategory,
  keyResultsForTask,
  sectionShowsNameField,
  splitExpectedResults,
  submittedClosureReason,
  validateActivityName,
  validateClosureReason,
  validateKeyResultSelections,
  validateStartDate,
} from '../../../../views/Activities/display';
import '../ActivityWizard/styles.css';

type EditTarget = { kind: 'section'; sectionId: string } | { kind: 'expectedResults' } | { kind: 'status' };

// Where a `?edit=` deep link lands. A module-level function of its inputs rather than a closure in the component,
// so the state initialiser and the effect that re-resolves once the schema loads share one definition and the
// effect can list everything it reads.
const resolveInitialTarget = (
  initialEdit: string | undefined,
  sections: readonly { id: string }[],
  forcedStatus: string | null,
  usesExpectedResults: boolean
): EditTarget | 'check' => {
  if (!initialEdit) {
    return 'check';
  }
  if (initialEdit === 'status') {
    return forcedStatus ? 'check' : { kind: 'status' };
  }
  if (initialEdit === 'expected-results' || initialEdit === 'expectedResults') {
    return usesExpectedResults ? { kind: 'expectedResults' } : 'check';
  }
  return sections.some((section) => section.id === initialEdit) ? { kind: 'section', sectionId: initialEdit } : 'check';
};

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
    closureReason: activity.closureReason ?? '',
  };

  const [state, dispatch] = useReducer(activityFormReducer, hydrated);

  const taxonomyQuery = useQuery({ queryKey: activityTaxonomyKey, queryFn: getActivityTaxonomy, staleTime: defaultStaleTime });
  const taxonomy = taxonomyQuery.data?.data;
  const schemaQuery = useQuery({
    queryKey: activityFormSchemaKeyFor(activity.categoryId),
    queryFn: () => getActivityFormSchema(activity.categoryId ?? undefined),
    enabled: !!activity.categoryId,
    staleTime: defaultStaleTime,
  });

  const isPeople = isPeopleCategory(activity.categoryName);
  const usesExpectedResults = categoryUsesExpectedResults(activity.categoryName);
  // Non-null for a category whose status is fixed rather than chosen (BLUM-7860 AC4). The editor keeps the same
  // three behaviours as the wizard — no measures target, no status target (BLUM-8090), no type card — because the
  // two derive their screens separately and have drifted before (BLUM-7792).
  const forcedStatus = forcedActivityStatus(activity.categoryName);
  const copy = activityCopy(isPeople, activity.categoryName);
  // The taxonomy is locked here, so this is a fixed fact about the record's category rather than a live selection.
  const singleTypeCategory = (taxonomy?.tasks ?? []).filter((task) => task.categoryId === activity.categoryId).length === 1;

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
  const sectionShowsName = (sectionId: string | undefined) => sectionShowsNameField(sections.find((section) => section.id === sectionId)?.title, activity.categoryName);
  // Which of the two measures screens this type gets. StepExpectedResults derives the same thing from the same
  // taxonomy rows, and the two MUST agree: since BLUM-7792 the free-text screen requires Measure 1 by position
  // while the checkbox screen counts selections, so a disagreement renders one screen and enforces the other
  // rule — which is how a cleared Measure 1 with Measure 2 filled would slip through. This used to read
  // `!taxonomy || …` to avoid discounting hydrated selections mid-load; the loading gate below now owns that
  // window in both hosts, so the flag can be the plain derivation the step uses.
  const keyResultOptions = taxonomy ? keyResultsForTask(taxonomy, state.taskId) : [];
  const hasStandardKeyResults = keyResultOptions.length > 0;

  const editTargets = useMemo<EditTarget[]>(() => {
    const targets: EditTarget[] = sections.map((section) => ({ kind: 'section', sectionId: section.id }));
    if (usesExpectedResults) {
      targets.push({ kind: 'expectedResults' });
    }
    if (!forcedStatus) {
      targets.push({ kind: 'status' });
    }
    return targets;
  }, [forcedStatus, sections, usesExpectedResults]);

  const [mode, setMode] = useState<EditTarget | 'check'>(() => resolveInitialTarget(initialEdit, sections, forcedStatus, usesExpectedResults));
  const [discarding, setDiscarding] = useState(false);
  const [hasPendingChanges, setHasPendingChanges] = useState(false);

  // The `?edit=` deep-link target depends on the (async) schema sections. If the schema isn't cached at mount
  // (e.g. a direct load/refresh on the edit URL), the useState initialiser above resolves to 'check'; re-resolve
  // once the sections load, applying the requested target a single time. The ref is what makes it a single time:
  // the other deps are fixed for one activity, and a later change to any of them must not pull the user back to
  // the deep-linked section after they have moved on.
  const initialApplied = useRef(false);
  useEffect(() => {
    if (initialApplied.current || sections.length === 0) {
      return;
    }
    initialApplied.current = true;
    const target = resolveInitialTarget(initialEdit, sections, forcedStatus, usesExpectedResults);
    if (target !== 'check') {
      setMode(target);
    }
  }, [sections, initialEdit, forcedStatus, usesExpectedResults]);

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

  // Editing a section, returning to check answers, and entering/leaving the discard interruption are all
  // in-page view changes. Scroll to the top and move focus to the new heading so keyboard and screen-reader
  // users encounter the replacement content from the top. While discarding, the heading is unmounted (the
  // ConfirmationPanel is shown and self-focuses its own heading), so getElementById no-ops here; on cancel
  // the heading is back and regains focus. The taxonomy gate below renders above BOTH headings, so on a cold
  // load (no cached taxonomy) the first run finds nothing — `isLoading` is in the deps so the effect runs again
  // once the content commits, which is also what announces the Loading→content swap. There is no live region
  // for that: "Loading…" mounts with the page, so an aria-live region would announce nothing on arrival.
  useLayoutEffect(() => {
    scrollToTop();
    document.getElementById('activity-editor-heading')?.focus({ preventScroll: true });
  }, [mode, discarding, taxonomyQuery.isLoading]);

  const validateTarget = (target: EditTarget): Record<string, string> => {
    switch (target.kind) {
      case 'section': {
        const errors = validateAnswers(sectionQuestions(target.sectionId), state.answers);
        // Edit/index.tsx only routes completed activities here (drafts go back through the wizard), so the name
        // is always required when the field is on screen; the isDraft check is a belt-and-braces guard.
        const nameError = sectionShowsName(target.sectionId) ? validateActivityName(state.title, !activity.isDraft) : null;
        if (nameError) {
          errors['activity-name'] = nameError;
        }
        return errors;
      }
      case 'expectedResults':
        return validateKeyResultSelections(state, hasStandardKeyResults);
      default: {
        const errors: Record<string, string> = {};
        if (!state.status) {
          errors['activity-status'] = copy.statusError;
        } else {
          const startError = validateStartDate(state.status, state.startMonth, state.startYear);
          if (startError) {
            errors[startError.field] = startError.message;
          }
          const closureError = validateClosureReason(state.status, state.closureReason, true, copy);
          if (closureError) {
            errors['activity-closure-reason'] = closureError;
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
    // A Joint partnership has no name field either since BLUM-8090, but its stored name IS written back, on
    // purpose: one saved before then keeps being listed by it, and with no field on screen state.title can only
    // hold what was loaded — never something typed here.
    title: isPeople ? null : state.title.trim() || null,
    categoryId: state.categoryId || null,
    taskId: state.taskId || null,
    customTaskText: state.customTaskText.trim() || null,
    // `|| null`, not `??`: state.status is '' when unset, which ?? would send as an empty string rather than
    // "no status chosen". Both handlers happen to use IsNullOrWhiteSpace today, so this is about keeping the
    // payload honest rather than a live bug.
    status: forcedStatus || state.status || null,
    startMonth: state.startMonth ? Number(state.startMonth) : null,
    startYear: state.startYear ? Number(state.startYear) : null,
    closureReason: submittedClosureReason(forcedStatus || state.status, state.closureReason),
    expectedResults: buildExpectedResultsPayload(state),
    answers: collectAnswers(allQuestions, state.answers),
    saveAsDraft: false,
  });

  const updateMutation = useMutation({
    mutationFn: updateActivity,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: activitiesListKey });
      queryClient.invalidateQueries({ queryKey: activityKey(activity.id) });
      invalidateReportCaches(queryClient, report.collectionId);
      navigate(report.listPath, { state: { banner: 'updated', name: activity.title } });
    },
    // A field error from the API arrives while the user is on the check screen, where that field is not mounted —
    // so its error-summary link would resolve to nothing. Jump back to the section that renders it, exactly as
    // handleSave already does for the client-side checks.
    onError: (error) => {
      const errors = mapActivityErrorFields(error);
      // Bumped so the error summary takes focus even when the same refusal comes back twice.
      dispatch({ type: 'BUMP_ATTEMPTS' });
      dispatch({ type: 'SET_ERRORS', errors });
      const owning = editTargets.find((target) =>
        Object.keys(errors).some((field) =>
          field === EXPECTED_RESULTS_ERROR_KEY ? target.kind === 'expectedResults' : field === 'activity-name' && target.kind === 'section' && sectionShowsName(target.sectionId)
        )
      );
      if (owning) {
        setMode(owning);
      }
    },
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
      if (question) {
        return { targetName: questionErrorTargetId(question), text: message as string };
      }
      if (key === EXPECTED_RESULTS_ERROR_KEY) {
        return { targetName: expectedResultsErrorTargetId(keyResultOptions[0]?.code), text: message as string };
      }
      return { targetName: key.toLowerCase(), text: message as string };
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
      return <StepDynamicSection section={section} questions={sectionQuestions(section.id)} state={state} dispatch={dispatch} showNameField={sectionShowsName(section.id)} />;
    }
    if (target.kind === 'expectedResults') {
      return <StepExpectedResults state={state} dispatch={dispatch} keyResults={taxonomy?.keyResults ?? []} />;
    }
    return <StepStatus state={state} dispatch={dispatch} isPeople={isPeople} categoryName={activity.categoryName} />;
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
        {/* Above the discard interruption, and emptied while that is showing, rather than inside the branch below.
            An error that was already showing is not focused again on "Go back to activity" — focus stays on the
            heading the layout effect above moves it to. A save refusal that lands while the interruption is up is
            a new one: its bump is held until the summary has it to show, and it takes focus on the way back. */}
        <ErrorSummary heading="There is a problem" errors={discarding ? [] : errorList} focusTrigger={state.submitAttempts} testId="activities-edit-error-summary" />
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
            {/* The measures step's screen AND its validation rule both come from the taxonomy, so rendering before
                it resolves shows one screen while enforcing the other's rule. The wizard has always gated on this;
                the editor did not, which left the whole of a fresh deep-link load (?edit=expected-results) in that
                state. */}
            {taxonomyQuery.isLoading ? (
              <p className="govuk-body" data-testid="activities-edit-loading">
                Loading…
              </p>
            ) : mode === 'check' ? (
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
                  showTypeCard={!singleTypeCategory}
                  statusLocked={!!forcedStatus}
                  expectedResultsStepIndex={usesExpectedResults ? editTargets.findIndex((t) => t.kind === 'expectedResults') : null}
                  statusStepIndex={forcedStatus ? null : editTargets.findIndex((t) => t.kind === 'status')}
                  additionalInformationPlacement="before-status"
                  onChange={(index) => setMode(editTargets[index])}
                />
                {hasPendingChanges && (
                  <div className="govuk-button-group">
                    <GovUKButton onClick={handleSave} isLoading={updateMutation.isPending} data-testid="activities-edit-save">
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
