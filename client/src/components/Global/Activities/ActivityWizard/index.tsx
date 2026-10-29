import React, { useEffect, useLayoutEffect, useMemo, useReducer, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, H1, useGovUKNotification } from '../../../GovUKComponents';
import StepActivityType from '../StepActivityType';
import StepDynamicSection from '../StepDynamicSection';
import StepExpectedResults from '../StepExpectedResults';
import StepStatus from '../StepStatus';
import StepCheckAnswers from '../StepCheckAnswers';
import ConfirmationPanel from '../ConfirmationPanel';
import { homeLinkForRole, useAuthProvider } from '../../../AuthProvider';
import useUnsavedChangesGuard from '../../../../hooks/useUnsavedChangesGuard';
import usePageTitle from '../../../../hooks/usePageTitle';
import scrollToTop from '../../../../helpers/scrollToTop';
import { activitiesListKey, activityFormSchemaKeyFor, activityKey, activityTaxonomyKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { canManageActivities } from '../../../../views/Activities/navigation';
import { invalidateReportCaches, useActivityReportContext } from '../../../../views/Activities/reportContext';
import { activityFormReducer, initialActivityFormState, type ActivityFormState } from '../../../../views/Activities/reducer';
import { getActivityFormSchema, getActivityTaxonomy } from '../../../../views/Activities/queries';
import { createActivity, updateActivity, type CreateActivityInput } from '../../../../views/Activities/mutations';
import { collectAnswers, questionErrorTargetId, validateAnswers } from '../../../../views/Activities/answers';
import { mapActivityErrorFields } from '../../../../views/Activities/messages';
import {
  ACTIVITY_BANNER_COPY,
  ADDITIONAL_INFORMATION_SECTION,
  activityCopy,
  activityTypeDisplayName,
  asksOneQuestionPerPage,
  buildExpectedResultsPayload,
  categoryCollectsName,
  categoryUsesExpectedResults,
  EXPECTED_RESULTS_ERROR_KEY,
  expectedResultsErrorTargetId,
  forcedActivityStatus,
  isActivitySectionApplicable,
  isJointPartnershipCategory,
  isOtherType,
  isPeopleCategory,
  keyResultsForTask,
  radioGroupErrorTargetId,
  sectionShowsNameField,
  submittedClosureReason,
  submittedStartDate,
  validateActivityName,
  validateClosureReason,
  validateKeyResultSelections,
  validateNameLength,
  validateStartDate,
} from '../../../../views/Activities/display';
import './styles.css';

type StepKind = 'taxonomy' | 'section' | 'expectedResults' | 'status' | 'check';
interface StepDescriptor {
  kind: StepKind;
  sectionId?: string;
  // Set when the section is asked one question per page (asksOneQuestionPerPage): the step renders and validates
  // this question alone, under the section's heading.
  questionId?: string;
}

// The interruption shown when the user leaves mid-setup (BLUM-7639). Saving is the primary action, so the
// answers survive by default; leaving without saving stays available for a start the user did not mean.
const EXIT_PANEL_TITLE = 'Do you want to save your progress?';
const EXIT_PANEL_BODY = 'Your answers so far will be saved as a draft. You can come back and finish this activity later.';

// Everything the user has actually entered. Errors and the attempt counter change on every validation run
// without being content, so they are excluded from the comparison that decides there is progress to lose.
const contentSnapshot = ({ errors: _errors, submitAttempts: _submitAttempts, ...content }: ActivityFormState): string => JSON.stringify(content);

interface Props {
  mode: 'create' | 'edit';
  activityId?: string;
  initialState?: Partial<ActivityFormState>;
  // Edit of a completed record: category and type are locked, so that step is skipped.
  lockTaxonomy?: boolean;
  allowSaveDraft?: boolean;
}

const ActivityWizard = ({ mode, activityId, initialState, lockTaxonomy = false, allowSaveDraft = true }: Props): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const { setNotification } = useGovUKNotification();
  // Set when the wizard was entered from Section 1 of a quarterly return — it decides the breadcrumbs and where
  // saving, cancelling or a role bounce lands the user (BLUM-6967).
  const report = useActivityReportContext();

  const [step, setStep] = useState(0);
  const [state, dispatch] = useReducer(activityFormReducer, { ...initialActivityFormState, ...initialState });

  useEffect(() => {
    if (userRole && !canManageActivities(userRole)) {
      navigate(report.listPath);
    }
  }, [userRole, navigate, report.listPath]);

  // Where leaving the flow lands: the activity page keeps the flow (it is an /activities/… URL, so it needs
  // the param); listPath already is the Section 1 URL and must not be decorated.
  const exitTarget = mode === 'edit' && activityId ? report.activityLink(ROUTES.activities.detail(activityId)) : report.listPath;

  // Sticky, and deliberately so: it arms the history sentinel in useUnsavedChangesGuard, which is pushed once
  // and cannot be taken back off when the user empties a field again.
  const [baseline] = useState(() => contentSnapshot({ ...initialActivityFormState, ...initialState }));
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  // One nullable object rather than a boolean plus a destination, deliberately: applyApiErrors and submit's
  // name-problem branch both close the panel, and a destination held separately would survive that. Click
  // "Reports", save, get a 400, correct the field, keep working, press Back ten minutes later — and "Leave
  // without saving" would honour a click the user has long forgotten. Clearing the panel clears the target.
  const [exit, setExit] = useState<{ to: string | null; seq: number } | null>(null);
  const exiting = exit !== null;
  // The mutation's onSuccess runs after the render that started it, so it reads the destination from here.
  // A link clicked while that save is still in flight therefore retargets it — most recent intent wins. The
  // panel's own buttons are disabled during a save because two saves would race; a changed destination cannot.
  const exitRef = useRef(exit);
  exitRef.current = exit;

  useEffect(() => {
    if (!hasUnsavedChanges && contentSnapshot(state) !== baseline) {
      setHasUnsavedChanges(true);
    }
  }, [state, baseline, hasUnsavedChanges]);

  // Leaving with nothing entered is not an accident worth interrupting — a freshly opened wizard just exits.
  // Errors are dropped on the way out for the same reason goBack drops them: cancelling back out of the panel
  // must not restore a summary that sits above the heading focus lands on.
  // `destination` is where the user asked to go — an intercepted link. Null for the Back button and the
  // in-page Back link, which name nowhere, and fall back to the flow's own exit.
  const requestExit = (rawDestination: string | null = null) => {
    // The crown logo points at "/", which renders Splash and immediately redirects again. That second
    // navigation is what clears a success notification (GovUKNotificationProvider allows exactly one), so a
    // draft saved on the way out would land with no confirmation. Resolving the hop away up front also spares
    // the user a render of a loading screen they never needed. homeLinkForRole answers ROUTES.root for a role
    // it does not know, which would leave the hop in place — fall through to the flow's own exit rather than
    // reinstate the bug this line exists to remove.
    const roleHome = homeLinkForRole(userRole);
    const home = roleHome === ROUTES.root ? exitTarget : roleHome;
    const destination = rawDestination === ROUTES.root ? home : rawDestination;
    dispatch({ type: 'SET_ERRORS', errors: {} });
    if (hasUnsavedChanges) {
      // seq, not a bare boolean: the panel is not a modal, so the header stays live and a second intercepted
      // link changes nothing it renders — without this, focus would be left on the link just activated and
      // nothing announced (WCAG 2.4.3).
      setExit((previous) => ({ to: destination, seq: (previous?.seq ?? 0) + 1 }));
      return;
    }
    navigate(destination ?? exitTarget);
  };

  useUnsavedChangesGuard({ enabled: hasUnsavedChanges, onBlockedExit: requestExit });

  const taxonomyQuery = useQuery({ queryKey: activityTaxonomyKey, queryFn: getActivityTaxonomy, staleTime: defaultStaleTime });
  const taxonomy = taxonomyQuery.data?.data;

  const schemaQuery = useQuery({
    queryKey: activityFormSchemaKeyFor(state.categoryId),
    queryFn: () => getActivityFormSchema(state.categoryId),
    enabled: !!state.categoryId,
    staleTime: defaultStaleTime,
  });

  const categoryName = taxonomy?.categories.find((category) => category.id === state.categoryId)?.name ?? '';
  const isPeople = isPeopleCategory(categoryName);
  const usesExpectedResults = categoryUsesExpectedResults(categoryName);
  // Non-null for a category whose status is fixed rather than chosen (BLUM-7860 AC4).
  const forcedStatus = forcedActivityStatus(categoryName);
  const copy = activityCopy(isPeople, categoryName);

  // Category routing selects one detail-screen variant; any independently type-scoped sections still react to
  // the selected activity type.
  const sections = useMemo(
    () =>
      (schemaQuery.data?.data.sections ?? [])
        .filter((section) => section.isActive)
        .filter((section) => isActivitySectionApplicable(section.title, categoryName))
        .filter((section) => !section.activityTaskId || section.activityTaskId === state.taskId)
        .sort((a, b) => a.displayOrder - b.displayOrder),
    [schemaQuery.data, state.taskId, categoryName]
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
  const stepQuestions = (descriptor: StepDescriptor) =>
    sectionQuestions(descriptor.sectionId!).filter((question) => !descriptor.questionId || question.id === descriptor.questionId);
  // The name field is part of the "Activity details" step body, so the step that renders it is the step that validates it.
  // No category that is asked one question per page collects a name, so a split section never repeats the field.
  const sectionShowsName = (sectionId: string | undefined) => sectionShowsNameField(sections.find((section) => section.id === sectionId)?.title, categoryName);
  const tasksForCategory = (taxonomy?.tasks ?? []).filter((task) => task.categoryId === state.categoryId);
  const selectedTask = tasksForCategory.find((task) => task.id === state.taskId);
  // A category offering exactly one type asks no type question — StepActivityType shows no reveal for it — so the
  // type has to be chosen here. An effect rather than a second dispatch inside the category's
  // onChange, because this also has to cover a draft hydrated with a category but no type — from an older client,
  // or from a category that has since lost types to reference-data drift — which no onChange would ever fire for.
  const onlyTaskId = tasksForCategory.length === 1 ? tasksForCategory[0].id : undefined;
  const singleTypeCategory = !!onlyTaskId;
  // The custom "Other" name only stands in for the type while an "Other ..." type is actually selected —
  // hydrated drafts may still carry a stale custom text from a previously selected type.
  const selectedTaskIsOther = !!selectedTask && isOtherType(selectedTask.name);
  // Which of the two measures screens this type gets. StepExpectedResults derives the same thing from the same
  // taxonomy rows, and the two MUST agree: since BLUM-7792 the free-text screen requires Measure 1 by position
  // while the checkbox screen counts selections, so a disagreement renders one screen and enforces the other
  // rule — which is how a cleared Measure 1 with Measure 2 filled would slip through. This used to read
  // `!taxonomy || …` to avoid discounting hydrated selections mid-load; the loading gate below now owns that
  // window in both hosts, so the flag can be the plain derivation the step uses.
  const keyResultOptions = taxonomy ? keyResultsForTask(taxonomy, state.taskId) : [];
  const hasStandardKeyResults = keyResultOptions.length > 0;

  useEffect(() => {
    if (onlyTaskId && !state.taskId) {
      dispatch({ type: 'SET_TASK', value: onlyTaskId, isOther: false });
    }
  }, [onlyTaskId, state.taskId]);

  // The fixed status is written into form state rather than only into the payload, so Check your answers reads
  // the same value every other category's does.
  useEffect(() => {
    if (forcedStatus && state.status !== forcedStatus) {
      dispatch({ type: 'SET_STATUS', value: forcedStatus });
    }
  }, [forcedStatus, state.status]);

  const steps = useMemo<StepDescriptor[]>(() => {
    const list: StepDescriptor[] = [];
    const detailSections = sections.filter((section) => section.title !== ADDITIONAL_INFORMATION_SECTION);
    const additionalInformationSections = sections.filter((section) => section.title === ADDITIONAL_INFORMATION_SECTION);
    const onePerPage = asksOneQuestionPerPage(categoryName);
    if (!lockTaxonomy) {
      list.push({ kind: 'taxonomy' });
    }
    detailSections.forEach((section) => {
      const questions = allQuestions.filter((question) => question.sectionId === section.id);
      if (onePerPage && questions.length > 0) {
        questions.forEach((question) => list.push({ kind: 'section', sectionId: section.id, questionId: question.id }));
      } else {
        list.push({ kind: 'section', sectionId: section.id });
      }
    });
    if (usesExpectedResults) {
      list.push({ kind: 'expectedResults' });
    }
    if (mode === 'edit') {
      additionalInformationSections.forEach((section) => list.push({ kind: 'section', sectionId: section.id }));
    }
    // A fixed status is not a question, and it collects no start date either (BLUM-8090), so there is no step.
    if (!forcedStatus) {
      list.push({ kind: 'status' });
    }
    if (mode === 'create') {
      additionalInformationSections.forEach((section) => list.push({ kind: 'section', sectionId: section.id }));
    }
    list.push({ kind: 'check' });
    return list;
  }, [allQuestions, categoryName, forcedStatus, lockTaxonomy, mode, sections, usesExpectedResults]);

  const current = steps[Math.min(step, steps.length - 1)];
  const isLastStep = current?.kind === 'check';
  const headingFor = (descriptor: StepDescriptor): string => {
    switch (descriptor.kind) {
      // Static: the step renders before a category is chosen, so the heading cannot switch on isPeople the way
      // the question inside it does (BLUM-7649).
      case 'taxonomy':
        return 'Activity type';
      case 'section':
        return sections.find((section) => section.id === descriptor.sectionId)?.title ?? 'Activity details';
      case 'expectedResults':
        return 'Expected results';
      case 'status':
        return copy.statusTitle;
      default:
        return 'Check your answers';
    }
  };
  const currentHeading = current ? headingFor(current) : mode === 'edit' ? 'Change activity' : 'Create activity';
  const hasErrors = Object.keys(state.errors).length > 0;

  usePageTitle(exiting ? EXIT_PANEL_TITLE : `${hasErrors ? 'Error: ' : ''}${currentHeading}`);

  // In-page step transitions don't change the route, so reset both the visual position and DOM focus
  // (advancing via Continue, going Back, or jumping from a check-answers "Change" link). Opening and closing
  // the exit panel is the same kind of in-place swap: the panel focuses its own heading, but cancelling out of
  // it changes no step, so without `exiting` here focus would be left on nothing at all.
  // `exit?.seq` covers the case neither of the others can see: intercepting a SECOND link while the panel is
  // already open changes nothing in the DOM, so nothing scrolls — and the panel focuses its heading with
  // preventScroll because scrolling is this effect's job. A user who had scrolled down to the footer to click
  // "Cookies" would otherwise get no visible response at all and a focus ring several screens above (2.4.7).
  useLayoutEffect(() => {
    scrollToTop();
    document.getElementById('activity-wizard-heading')?.focus({ preventScroll: true });
  }, [step, exiting, exit?.seq]);

  // The name is only collected for categories that have one (categoryCollectsName), and changing the category keeps
  // whatever was typed — so a name entered before switching to People-and-roles or Joint partnership is unreachable:
  // no step renders the field and check-answers hides the row. Don't submit what the user can't see or clear; the
  // backend caps it at 255, so a stale over-long name would 400 with no field to correct it. While the taxonomy
  // loads categoryName is empty and categoryCollectsName is true, so a draft's name is never silently dropped.
  //
  // The one name a Joint partnership still sends is the one its draft was loaded with: a partnership saved before
  // BLUM-8090 removed the field keeps being listed by its name (the migration leaves it), and resaving must not
  // strip it. A name typed this session never qualifies — hence the category check against the loaded draft too.
  const [loadedDraft] = useState(() => ({ categoryId: initialState?.categoryId ?? '', title: initialState?.title?.trim() || null }));
  const submittedTitle = categoryCollectsName(categoryName)
    ? state.title.trim() || null
    : isJointPartnershipCategory(categoryName) && state.categoryId === loadedDraft.categoryId
    ? loadedDraft.title
    : null;

  const buildPayload = (saveAsDraft: boolean): CreateActivityInput => ({
    title: submittedTitle,
    categoryId: state.categoryId || null,
    taskId: state.taskId || null,
    customTaskText: !selectedTask || selectedTaskIsOther ? state.customTaskText.trim() || null : null,
    // The effect above has normally already written this into state; forcedStatus wins here as well so a draft
    // saved from the first render — before the effect commits — is still saved with the right status.
    // `|| null`, not `??`: state.status is '' when unset, which ?? would send as an empty string rather than
    // "no status chosen". Both handlers happen to use IsNullOrWhiteSpace today, so this is about keeping the
    // payload honest rather than a live bug.
    status: forcedStatus || state.status || null,
    ...submittedStartDate(forcedStatus, state.startMonth, state.startYear),
    closureReason: submittedClosureReason(forcedStatus || state.status, state.closureReason),
    expectedResults: buildExpectedResultsPayload(state),
    answers: collectAnswers(allQuestions, state.answers),
    saveAsDraft,
  });

  // Mirrors what was actually saved, so the success banner can't name an activity by a title we dropped above.
  const savedTaskName = tasksForCategory.find((task) => task.id === state.taskId)?.name;
  const displayName = submittedTitle || (savedTaskName ? activityTypeDisplayName(savedTaskName) : undefined) || 'activity';

  const onSuccess = (banner: 'created' | 'updated' | 'draft') => {
    queryClient.invalidateQueries({ queryKey: activitiesListKey });
    if (activityId) {
      queryClient.invalidateQueries({ queryKey: activityKey(activityId) });
    }
    // The report's task-list statuses and reportable-activity list are derived from the activities, so they are
    // stale the moment one is added or changed from inside the flow.
    invalidateReportCaches(queryClient, report.collectionId);
    // Saved on the way out of an intercepted link, so the user goes where they clicked. The route-state banner
    // cannot follow them — only the activities list and Section 1 read it — and losing it would leave the save
    // with no confirmation at all (WCAG 4.1.3). A success notification survives exactly one navigation
    // (navAllowanceRef in GovUKNotificationProvider) and renders from layouts/General/Content, so it arrives
    // on whichever GeneralLayout page they asked for.
    const destination = exitRef.current?.to;
    if (destination) {
      // Not `copy`: that name already means activityCopy(...) everywhere else in this file.
      const bannerCopy = ACTIVITY_BANNER_COPY[banner];
      setNotification({ type: 'success', title: bannerCopy.title, message: bannerCopy.body(displayName), testId: 'activities-create-banner' });
      navigate(destination);
      return;
    }
    // Back to Section 1 when that is where we came from; the list there renders the same success banner.
    navigate(report.listPath, { state: { banner, name: displayName } });
  };

  // A field error from the API arrives while the user is on Check your answers, where that field is not mounted —
  // so its error-summary link would resolve to nothing. Jump to the step that renders it first, exactly as
  // submit() already does for the client-side name checks.
  const stepOwningField = (field: string): number => {
    if (field === 'activity-name') {
      return steps.findIndex((descriptor) => descriptor.kind === 'section' && sectionShowsName(descriptor.sectionId));
    }
    if (field === EXPECTED_RESULTS_ERROR_KEY) {
      return steps.findIndex((descriptor) => descriptor.kind === 'expectedResults');
    }
    return -1;
  };

  const applyApiErrors = (error: unknown) => {
    const errors = mapActivityErrorFields(error);
    // A refusal has a field to correct, so the exit panel must give way to the error summary behind it. This
    // also drops any destination an intercepted link put there, which is the point — see the state above.
    setExit(null);
    // Bumped so the error summary takes focus even when the same refusal comes back twice; the step change
    // below focuses the heading first (useLayoutEffect), then this effect moves focus onto the summary.
    dispatch({ type: 'BUMP_ATTEMPTS' });
    dispatch({ type: 'SET_ERRORS', errors });
    const owningStep = Object.keys(errors)
      .map(stepOwningField)
      .find((index) => index >= 0);
    if (owningStep !== undefined) {
      setStep(owningStep);
    }
  };

  const createMutation = useMutation({
    mutationFn: createActivity,
    onSuccess: (_response, variables) => onSuccess(variables.saveAsDraft ? 'draft' : 'created'),
    onError: applyApiErrors,
  });

  const updateMutation = useMutation({
    mutationFn: updateActivity,
    onSuccess: (_response, variables) => onSuccess(variables.saveAsDraft ? 'draft' : 'updated'),
    onError: applyApiErrors,
  });

  // None of the save paths run the step validation, but the backend caps both free-text names on drafts too, so
  // an over-long one would 400 — and since the 13 August protocol it also refuses a completed activity with no
  // name at all. Each problem pairs the error with the step that renders its field — the error summary links to
  // the input's id, so it must never be raised on a step where that input isn't mounted.
  const nameProblem = (saveAsDraft: boolean): { step: number; errors: Record<string, string> } | null => {
    const titleStep = steps.findIndex((descriptor) => descriptor.kind === 'section' && sectionShowsName(descriptor.sectionId));
    const titleError = titleStep >= 0 ? validateActivityName(state.title, !saveAsDraft) : null;
    if (titleError && titleStep >= 0) {
      return { step: titleStep, errors: { 'activity-name': titleError } };
    }
    const otherError = selectedTaskIsOther ? validateNameLength(state.customTaskText, copy.otherNameLabel) : null;
    const otherStep = steps.findIndex((descriptor) => descriptor.kind === 'taxonomy');
    if (otherError && otherStep >= 0) {
      return { step: otherStep, errors: { 'activity-other-task': otherError } };
    }
    return null;
  };

  // The closure reason follows the same rules as the name: capped on drafts too, and required to complete a Closed
  // activity (BLUM-7673). The status step validates it on Continue, but Save as draft can be pressed on any step.
  const closureReasonProblem = (saveAsDraft: boolean): { step: number; errors: Record<string, string> } | null => {
    const statusStep = steps.findIndex((descriptor) => descriptor.kind === 'status');
    const closureError = statusStep >= 0 ? validateClosureReason(state.status, state.closureReason, !saveAsDraft, copy) : null;
    return closureError ? { step: statusStep, errors: { 'activity-closure-reason': closureError } } : null;
  };

  const submit = (saveAsDraft: boolean) => {
    const problem = nameProblem(saveAsDraft) ?? closureReasonProblem(saveAsDraft);
    if (problem) {
      setExit(null);
      dispatch({ type: 'BUMP_ATTEMPTS' });
      dispatch({ type: 'SET_ERRORS', errors: problem.errors });
      setStep(problem.step);
      return;
    }
    const payload = buildPayload(saveAsDraft);
    if (mode === 'edit' && activityId) {
      updateMutation.mutate({ ...payload, id: activityId });
    } else {
      createMutation.mutate(payload);
    }
  };

  const isSubmitting = createMutation.isPending || updateMutation.isPending;

  const validateCurrent = (): Record<string, string> => {
    if (!current) {
      return {};
    }
    switch (current.kind) {
      case 'taxonomy': {
        if (!state.categoryId) {
          return { 'activity-category': 'Select a category' };
        }
        const task = tasksForCategory.find((item) => item.id === state.taskId);
        if (!state.taskId) {
          return { 'activity-type': copy.typeError };
        }
        if (task && isOtherType(task.name)) {
          if (!state.customTaskText.trim()) {
            return { 'activity-other-task': copy.otherError };
          }
          const lengthError = validateNameLength(state.customTaskText, copy.otherNameLabel);
          if (lengthError) {
            return { 'activity-other-task': lengthError };
          }
        }
        return {};
      }
      case 'section': {
        const errors = validateAnswers(stepQuestions(current), state.answers);
        const nameError = sectionShowsName(current.sectionId) ? validateActivityName(state.title, true) : null;
        if (nameError) {
          errors['activity-name'] = nameError;
        }
        return errors;
      }
      case 'expectedResults':
        return validateKeyResultSelections(state, hasStandardKeyResults);
      case 'status': {
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
      default:
        return {};
    }
  };

  const goNext = () => {
    const errors = validateCurrent();
    dispatch({ type: 'BUMP_ATTEMPTS' });
    dispatch({ type: 'SET_ERRORS', errors });
    if (Object.keys(errors).length === 0) {
      setStep((value) => value + 1);
    }
  };

  const goBack = () => {
    if (exiting) {
      setExit(null);
      return;
    }
    if (step === 0) {
      requestExit();
      return;
    }
    dispatch({ type: 'SET_ERRORS', errors: {} });
    setStep((value) => value - 1);
  };

  const handlePrimary = () => {
    if (isLastStep) {
      submit(false);
    } else {
      goNext();
    }
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
      // Neither radio group on the Activity type step has an element carrying the bare error key as its id.
      if (key === 'activity-category') {
        return { targetName: radioGroupErrorTargetId(key, taxonomy?.categories[0]?.id), text: message as string };
      }
      if (key === 'activity-type') {
        // Reference-data drift can leave a category with no active types (the seeder logs and skips a missing
        // one), and then there is no type radio to reach — fall back to the category radio, which is always on
        // the page.
        const typeTarget = tasksForCategory[0] ? radioGroupErrorTargetId(key, tasksForCategory[0].id) : radioGroupErrorTargetId('activity-category', state.categoryId);
        return { targetName: typeTarget, text: message as string };
      }
      return { targetName: key.toLowerCase(), text: message as string };
    });

  const captionText = mode === 'edit' ? 'Change activity' : 'Create activity';

  const answerSections = sections.map((section) => ({
    section,
    questions: sectionQuestions(section.id),
    stepIndex: steps.findIndex((descriptor) => descriptor.kind === 'section' && descriptor.sectionId === section.id),
    showNameField: sectionShowsName(section.id),
  }));

  const renderStep = (descriptor: StepDescriptor): React.ReactNode => {
    switch (descriptor.kind) {
      case 'taxonomy':
        return <StepActivityType state={state} dispatch={dispatch} categories={taxonomy?.categories ?? []} tasks={taxonomy?.tasks ?? []} />;
      case 'section': {
        const section = sections.find((item) => item.id === descriptor.sectionId)!;
        return <StepDynamicSection section={section} questions={stepQuestions(descriptor)} state={state} dispatch={dispatch} showNameField={sectionShowsName(section.id)} />;
      }
      case 'expectedResults':
        return <StepExpectedResults state={state} dispatch={dispatch} keyResults={taxonomy?.keyResults ?? []} />;
      case 'status':
        return <StepStatus state={state} dispatch={dispatch} isPeople={isPeople} categoryName={categoryName} />;
      default:
        return (
          <StepCheckAnswers
            state={state}
            taxonomy={taxonomy}
            isPeople={isPeople}
            answerSections={answerSections}
            categoryStepIndex={lockTaxonomy ? null : steps.findIndex((d) => d.kind === 'taxonomy')}
            showTypeCard={!singleTypeCategory}
            statusLocked={!!forcedStatus}
            typeStepIndex={lockTaxonomy ? null : steps.findIndex((d) => d.kind === 'taxonomy')}
            expectedResultsStepIndex={usesExpectedResults ? steps.findIndex((d) => d.kind === 'expectedResults') : null}
            statusStepIndex={forcedStatus ? null : steps.findIndex((d) => d.kind === 'status')}
            additionalInformationPlacement={mode === 'edit' ? 'before-status' : 'after-status'}
            onChange={(target) => setStep(target)}
          />
        );
    }
  };

  // Check-answers buttons follow the protocol's 1.ABCDEF.1.6: [Save activity] / [Save as draft]; intermediate
  // steps keep [Continue] / [Save and come back later].
  const primaryLabel = isLastStep ? (mode === 'edit' ? 'Save changes' : 'Save activity') : 'Continue';
  const draftLabel = isLastStep ? 'Save as draft' : 'Save and come back later';
  const schemaLoading = !!state.categoryId && schemaQuery.isLoading;
  const wizardClassName = ['activity-wizard', isLastStep ? 'activity-wizard--check' : '', current?.kind === 'status' ? 'activity-wizard--status' : ''].filter(Boolean).join(' ');

  return (
    // The Back link renders in the layout chrome, above #main-content, so "Skip to main content" bypasses it
    // instead of landing on it (BLUM-7256).
    <GeneralLayout
      breadcrumbs={report.inReportFlow ? report.breadcrumbs(userRole, [{ label: captionText, link: '#' }]) : undefined}
      backLink={{ onClick: goBack, testId: 'activities-create-back', className: 'activity-wizard__back-link' }}>
      <div className={wizardClassName}>
        {/* Above the exit panel, and emptied while it is showing, rather than inside the branch below: a refusal
            closes the panel and sets its errors in one go, and a summary that only mounted at that moment would
            miss the bump that asks it to take focus. */}
        <ErrorSummary heading="There is a problem" errors={exiting ? [] : errorList} testId="activities-create-error-summary" focusTrigger={state.submitAttempts} />
        {exiting ? (
          <ConfirmationPanel
            testId="activities-create-exit"
            title={EXIT_PANEL_TITLE}
            body={EXIT_PANEL_BODY}
            confirmLabel="Save as draft and exit"
            onConfirm={() => submit(true)}
            secondaryAction={{ label: 'Leave without saving', onClick: () => navigate(exit?.to ?? exitTarget, { replace: true }), testId: 'activities-create-exit-leave' }}
            cancelLabel="Cancel"
            onCancel={() => setExit(null)}
            isLoading={isSubmitting}
            focusTrigger={exit?.seq}
          />
        ) : (
          <>
            <div className="activity-wizard__title-block">
              <span className="govuk-caption-l">{captionText}</span>
              <H1 className="activity-wizard__heading" id="activity-wizard-heading" tabIndex={-1}>
                {currentHeading}
              </H1>
            </div>

            {taxonomyQuery.isLoading ? (
              <p className="govuk-body" data-testid="activities-create-loading">
                Loading…
              </p>
            ) : (
              <>
                {current && renderStep(current)}

                {isLastStep && (
                  <p className="govuk-body activity-wizard__check-guidance">
                    Save the activity when you’ve checked your answers. If the activity status is closed or finished, you will not be able to edit it again. Save as a draft if you
                    want to come back later.
                  </p>
                )}

                <div className="govuk-button-group">
                  <GovUKButton onClick={handlePrimary} isLoading={isSubmitting} disabled={schemaLoading} data-testid="activities-create-continue">
                    {primaryLabel}
                  </GovUKButton>
                  {allowSaveDraft && (
                    <GovUKButton className="govuk-button govuk-button--secondary" onClick={() => submit(true)} isLoading={isSubmitting} data-testid="activities-create-save-draft">
                      {draftLabel}
                    </GovUKButton>
                  )}
                </div>
              </>
            )}
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default ActivityWizard;
