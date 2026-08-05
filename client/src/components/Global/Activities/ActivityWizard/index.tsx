import React, { useEffect, useLayoutEffect, useMemo, useReducer, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, H1 } from '../../../GovUKComponents';
import StepCategory from '../StepCategory';
import StepType from '../StepType';
import StepDynamicSection from '../StepDynamicSection';
import StepExpectedResults from '../StepExpectedResults';
import StepStatus from '../StepStatus';
import StepCheckAnswers from '../StepCheckAnswers';
import { useAuthProvider } from '../../../AuthProvider';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import usePageTitle from '../../../../hooks/usePageTitle';
import scrollToTop from '../../../../helpers/scrollToTop';
import { activitiesListKey, activityKey, activityTaxonomyKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { canManageActivities } from '../../../../views/Activities/navigation';
import { invalidateReportCaches, useActivityReportContext } from '../../../../views/Activities/reportContext';
import { activityFormReducer, initialActivityFormState, type ActivityFormState } from '../../../../views/Activities/reducer';
import { getActivityFormSchema, getActivityTaxonomy } from '../../../../views/Activities/queries';
import { createActivity, updateActivity, type CreateActivityInput } from '../../../../views/Activities/mutations';
import { collectAnswers, questionErrorTargetId, validateActivityAnswers } from '../../../../views/Activities/answers';
import { mapActivityErrorFields } from '../../../../views/Activities/messages';
import {
  ADDITIONAL_INFORMATION_SECTION,
  activityCopy,
  activityTypeDisplayName,
  buildExpectedResultsPayload,
  categoryUsesExpectedResults,
  isActivitySectionApplicable,
  isNeedsAssessmentCategory,
  isOtherType,
  isPeopleCategory,
  keyResultsForTask,
  sectionShowsNameField,
  validateKeyResultSelections,
  validateNameLength,
  validateStartDate,
} from '../../../../views/Activities/display';
import './styles.css';

type StepKind = 'category' | 'type' | 'section' | 'expectedResults' | 'status' | 'check';
interface StepDescriptor {
  kind: StepKind;
  sectionId?: string;
}

interface Props {
  mode: 'create' | 'edit';
  activityId?: string;
  initialState?: Partial<ActivityFormState>;
  // Edit of a completed record: category and type are locked, so those steps are skipped.
  lockTaxonomy?: boolean;
  allowSaveDraft?: boolean;
}

const ActivityWizard = ({ mode, activityId, initialState, lockTaxonomy = false, allowSaveDraft = true }: Props): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
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

  const taxonomyQuery = useQuery(activityTaxonomyKey, getActivityTaxonomy, { staleTime: defaultStaleTime });
  const taxonomy = taxonomyQuery.data?.data;

  const schemaQuery = useQuery(['activity-form-schema', state.categoryId], () => getActivityFormSchema(state.categoryId), {
    enabled: !!state.categoryId,
    staleTime: defaultStaleTime,
  });

  const categoryName = taxonomy?.categories.find((category) => category.id === state.categoryId)?.name ?? '';
  const isPeople = isPeopleCategory(categoryName);
  const isNeedsAssessment = isNeedsAssessmentCategory(categoryName);
  const usesExpectedResults = categoryUsesExpectedResults(categoryName);
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
  // The name field is part of the "Activity details" step body, so the step that renders it is the step that validates it.
  const sectionShowsName = (sectionId: string | undefined) => sectionShowsNameField(sections.find((section) => section.id === sectionId)?.title, isPeople);
  const tasksForCategory = (taxonomy?.tasks ?? []).filter((task) => task.categoryId === state.categoryId);
  const selectedTask = tasksForCategory.find((task) => task.id === state.taskId);
  // The custom "Other" name only stands in for the type while an "Other ..." type is actually selected —
  // hydrated drafts may still carry a stale custom text from a previously selected type.
  const selectedTaskIsOther = !!selectedTask && isOtherType(selectedTask.name);
  const selectedTypeName = (selectedTaskIsOther ? state.customTaskText.trim() : '') || (selectedTask ? activityTypeDisplayName(selectedTask.name) : undefined);
  // Treat the type as having standard key-result options while the taxonomy is still loading so hydrated
  // selections are never discounted.
  const hasStandardKeyResults = !taxonomy || keyResultsForTask(taxonomy, state.taskId).length > 0;

  const steps = useMemo<StepDescriptor[]>(() => {
    const list: StepDescriptor[] = [];
    const detailSections = sections.filter((section) => section.title !== ADDITIONAL_INFORMATION_SECTION);
    const additionalInformationSections = sections.filter((section) => section.title === ADDITIONAL_INFORMATION_SECTION);
    if (!lockTaxonomy) {
      list.push({ kind: 'category' });
      list.push({ kind: 'type' });
    }
    detailSections.forEach((section) => list.push({ kind: 'section', sectionId: section.id }));
    if (usesExpectedResults) {
      list.push({ kind: 'expectedResults' });
    }
    if (mode === 'edit') {
      additionalInformationSections.forEach((section) => list.push({ kind: 'section', sectionId: section.id }));
    }
    list.push({ kind: 'status' });
    if (mode === 'create') {
      additionalInformationSections.forEach((section) => list.push({ kind: 'section', sectionId: section.id }));
    }
    list.push({ kind: 'check' });
    return list;
  }, [lockTaxonomy, mode, sections, usesExpectedResults]);

  const current = steps[Math.min(step, steps.length - 1)];
  const isLastStep = current?.kind === 'check';
  const headingFor = (descriptor: StepDescriptor): string => {
    switch (descriptor.kind) {
      case 'category':
        return 'Activity category';
      case 'type':
        return isPeople ? 'Role type' : 'Activity type';
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

  usePageTitle(`${hasErrors ? 'Error: ' : ''}${currentHeading}`);
  useErrorSummaryFocus(state.submitAttempts, hasErrors);

  // In-page step transitions don't change the route, so reset both the visual position and DOM focus
  // (advancing via Continue, going Back, or jumping from a check-answers "Change" link).
  useLayoutEffect(() => {
    scrollToTop();
    document.getElementById('activity-wizard-heading')?.focus({ preventScroll: true });
  }, [step]);

  // The name is only ever collected for non-People categories, and changing the category keeps whatever was
  // typed — so a name entered before switching to People-and-roles is unreachable: no step renders the field and
  // check-answers hides the row. Don't submit what the user can't see or clear; the backend caps it at 255, so a
  // stale over-long name would 400 with no field to correct it. While the taxonomy loads categoryName is empty,
  // so isPeople is false and the name is still submitted — a draft's name is never silently dropped.
  const submittedTitle = isPeople ? null : state.title.trim() || null;

  const buildPayload = (saveAsDraft: boolean): CreateActivityInput => ({
    title: submittedTitle,
    categoryId: state.categoryId || null,
    taskId: state.taskId || null,
    customTaskText: !selectedTask || selectedTaskIsOther ? state.customTaskText.trim() || null : null,
    status: state.status || null,
    startMonth: state.startMonth ? Number(state.startMonth) : null,
    startYear: state.startYear ? Number(state.startYear) : null,
    expectedResults: buildExpectedResultsPayload(state),
    answers: collectAnswers(allQuestions, state.answers),
    saveAsDraft,
  });

  // Mirrors what was actually saved, so the success banner can't name an activity by a title we dropped above.
  const savedTaskName = tasksForCategory.find((task) => task.id === state.taskId)?.name;
  const displayName = submittedTitle || (savedTaskName ? activityTypeDisplayName(savedTaskName) : undefined) || 'activity';

  const onSuccess = (banner: 'created' | 'updated' | 'draft') => {
    queryClient.invalidateQueries(activitiesListKey);
    if (activityId) {
      queryClient.invalidateQueries(activityKey(activityId));
    }
    // The report's task-list statuses and reportable-activity list are derived from the activities, so they are
    // stale the moment one is added or changed from inside the flow.
    invalidateReportCaches(queryClient, report.collectionId);
    // Back to Section 1 when that is where we came from; the list there renders the same success banner.
    navigate(report.listPath, { state: { banner, name: displayName } });
  };

  const createMutation = useMutation(createActivity, {
    onSuccess: (_response, variables) => onSuccess(variables.saveAsDraft ? 'draft' : 'created'),
    onError: (error) => dispatch({ type: 'SET_ERRORS', errors: mapActivityErrorFields(error) }),
  });

  const updateMutation = useMutation(updateActivity, {
    onSuccess: (_response, variables) => onSuccess(variables.saveAsDraft ? 'draft' : 'updated'),
    onError: (error) => dispatch({ type: 'SET_ERRORS', errors: mapActivityErrorFields(error) }),
  });

  // None of the save paths run the step validation, but the backend caps both free-text names on drafts too, so
  // an over-long one would 400. Each problem pairs the error with the step that renders its field — the error
  // summary links to the input's id, so it must never be raised on a step where that input isn't mounted.
  const nameLengthProblem = (): { step: number; errors: Record<string, string> } | null => {
    const titleError = validateNameLength(state.title, 'Activity name');
    const titleStep = steps.findIndex((descriptor) => descriptor.kind === 'section' && sectionShowsName(descriptor.sectionId));
    if (titleError && titleStep >= 0) {
      return { step: titleStep, errors: { 'activity-name': titleError } };
    }
    const otherError = selectedTaskIsOther ? validateNameLength(state.customTaskText, copy.otherNameLabel) : null;
    const otherStep = steps.findIndex((descriptor) => descriptor.kind === 'type');
    if (otherError && otherStep >= 0) {
      return { step: otherStep, errors: { 'activity-other-task': otherError } };
    }
    return null;
  };

  const submit = (saveAsDraft: boolean) => {
    const problem = nameLengthProblem();
    if (problem) {
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

  const isSubmitting = createMutation.isLoading || updateMutation.isLoading;

  const validateCurrent = (): Record<string, string> => {
    if (!current) {
      return {};
    }
    switch (current.kind) {
      case 'category':
        return state.categoryId ? {} : { 'activity-category': 'Select a category' };
      case 'type': {
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
        const errors = validateActivityAnswers(sectionQuestions(current.sectionId!), state.answers);
        const nameError = sectionShowsName(current.sectionId) ? validateNameLength(state.title, 'Activity name') : null;
        if (nameError) {
          errors['activity-name'] = nameError;
        }
        return errors;
      }
      case 'expectedResults':
        return validateKeyResultSelections(state, hasStandardKeyResults, isNeedsAssessment, !!taxonomy);
      case 'status': {
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
    dispatch({ type: 'SET_ERRORS', errors: {} });
    if (step === 0) {
      // The activity page keeps the flow (it is an /activities/… URL, so it needs the param); listPath already is
      // the Section 1 URL and must not be decorated.
      navigate(mode === 'edit' && activityId ? report.activityLink(ROUTES.activities.detail(activityId)) : report.listPath);
    } else {
      setStep((value) => value - 1);
    }
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
      return { targetName: question ? questionErrorTargetId(question) : key.toLowerCase(), text: message as string };
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
      case 'category':
        return <StepCategory state={state} dispatch={dispatch} categories={taxonomy?.categories ?? []} />;
      case 'type':
        return <StepType state={state} dispatch={dispatch} tasks={tasksForCategory} isPeople={isPeople} categoryName={categoryName} />;
      case 'section': {
        const section = sections.find((item) => item.id === descriptor.sectionId)!;
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
      case 'expectedResults':
        return <StepExpectedResults state={state} dispatch={dispatch} keyResults={taxonomy?.keyResults ?? []} isNeedsAssessment={isNeedsAssessment} />;
      case 'status':
        return <StepStatus state={state} dispatch={dispatch} isPeople={isPeople} />;
      default:
        return (
          <StepCheckAnswers
            state={state}
            taxonomy={taxonomy}
            isPeople={isPeople}
            answerSections={answerSections}
            categoryStepIndex={lockTaxonomy ? null : steps.findIndex((d) => d.kind === 'category')}
            typeStepIndex={lockTaxonomy ? null : steps.findIndex((d) => d.kind === 'type')}
            expectedResultsStepIndex={usesExpectedResults ? steps.findIndex((d) => d.kind === 'expectedResults') : null}
            statusStepIndex={steps.findIndex((d) => d.kind === 'status')}
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
  const wizardClassName = ['activity-wizard', isLastStep ? 'activity-wizard--check' : '', current?.kind === 'status' ? 'activity-wizard--status' : '']
    .filter(Boolean)
    .join(' ');

  return (
    // The Back link renders in the layout chrome, above #main-content, so "Skip to main content" bypasses it
    // instead of landing on it (BLUM-7256).
    <GeneralLayout
      breadcrumbs={report.inReportFlow ? report.breadcrumbs(userRole, [{ label: captionText, link: '#' }]) : undefined}
      backLink={{ onClick: goBack, testId: 'activities-create-back', className: 'activity-wizard__back-link' }}>
      <div className={wizardClassName}>
        {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

        <div className="activity-wizard__title-block">
          <span className="govuk-caption-l">{captionText}</span>
          <H1 className="activity-wizard__heading" id="activity-wizard-heading" tabIndex={-1}>
            {currentHeading}
          </H1>
        </div>

        {taxonomyQuery.isLoading ? (
          <p className="govuk-body">Loading…</p>
        ) : (
          <>
            {current && renderStep(current)}

            {isLastStep && (
              <p className="govuk-body activity-wizard__check-guidance">
                Save the activity when you’ve checked your answers. If the activity status is closed or finished, you will not be able to edit it again. Save as a draft if you want
                to come back later.
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
      </div>
    </GeneralLayout>
  );
};

export default ActivityWizard;
