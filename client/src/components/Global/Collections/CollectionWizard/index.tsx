import React, { useLayoutEffect, useMemo, useReducer, useState } from 'react';
import useFeature from '../../../../hooks/useFeature';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GovUKBackLink, GovUKButton, GovUKErrorSummary, H1 } from '../../../GovUKComponents';
import StepDetails from '../StepDetails';
import StepWindow from '../StepWindow';
import StepForms from '../StepForms';
import StepReportingLas from '../StepReportingLas';
import StepReminders from '../StepReminders';
import StepCheckAnswers from '../StepCheckAnswers';
import usePageTitle from '../../../../hooks/usePageTitle';
import scrollToTop from '../../../../helpers/scrollToTop';
import { collectionFormOptionsKey, collectionKey, collectionReportingLasKey, collectionsListKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import {
  buildCollectionErrorList,
  buildCreateInput,
  buildUpdateInput,
  collectionFormReducer,
  formOptionsForType,
  initialCollectionFormState,
  validateCollectionSection,
  type CollectionFormState,
} from '../../../../views/Admin/Collections/reducer';
import { getCollectionFormOptions, getReportingLas } from '../../../../views/Admin/Collections/queries';
import { createCollection, updateCollection } from '../../../../views/Admin/Collections/mutations';
import { isCollectionNameTakenError, mapCollectionError } from '../../../../views/Admin/Collections/messages';
import { useDefaultCollectionForms } from '../useDefaultCollectionForms';
import './styles.css';

type StepKind = 'details' | 'window' | 'forms' | 'reportingLas' | 'reminders' | 'check';

interface Props {
  mode: 'create' | 'edit';
  collectionId?: string;
  initialState?: Partial<CollectionFormState>;
  initialStep?: number;
  allowSaveDraft?: boolean;
}

export interface CollectionWizardDiscardState {
  formState: CollectionFormState;
  step: number;
}

const HEADINGS: Record<StepKind, string> = {
  details: 'Data collection details',
  window: 'Collection window',
  forms: 'Forms',
  reportingLas: 'Reporting organisations',
  reminders: 'Automatic reminders',
  check: 'Check your answers',
};

const TEST_ID = 'admin-collections-create-';

const CollectionWizard = ({ mode, collectionId, initialState, initialStep = 0, allowSaveDraft = true }: Props): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [step, setStep] = useState(initialStep);
  const [state, dispatch] = useReducer(collectionFormReducer, { ...initialCollectionFormState, ...initialState });
  // BLUM-9103: the per-form version choice is offered, and so sent, only while form versioning is on.
  const formVersionsEnabled = useFeature('BLUM_9087');

  const formOptionsQuery = useQuery({ queryKey: collectionFormOptionsKey, queryFn: getCollectionFormOptions, staleTime: defaultStaleTime });
  // Only the chosen type's forms are offered (BLUM-7943), and all of them start ticked.
  const formOptions = useMemo(() => formOptionsForType(formOptionsQuery.data?.data ?? [], state.type), [formOptionsQuery.data, state.type]);
  useDefaultCollectionForms(state, dispatch, formOptions, formOptionsQuery.isSuccess);
  const reportingLasQuery = useQuery({ queryKey: collectionReportingLasKey, queryFn: getReportingLas, staleTime: defaultStaleTime });
  const reportingLas = reportingLasQuery.data?.data ?? [];

  const steps = useMemo<StepKind[]>(() => ['details', 'window', 'forms', 'reportingLas', 'reminders', 'check'], []);
  const current = steps[Math.min(step, steps.length - 1)];
  const isLastStep = current === 'check';

  const captionText = mode === 'edit' ? 'Change data collection' : 'Add data collection';
  const hasErrors = Object.keys(state.errors).length > 0;
  usePageTitle(`${hasErrors && state.submitAttempts > 0 ? 'Error: ' : ''}${HEADINGS[current]}`);

  // In-page step transitions don't change the route, so reset both the visual position and DOM focus
  // (the Continue button persists across steps, so focus would otherwise stay on it). Move focus to the
  // new step's heading so keyboard and screen-reader users start from the top of the replacement content.
  useLayoutEffect(() => {
    scrollToTop();
    document.getElementById('collection-wizard-heading')?.focus({ preventScroll: true });
  }, [step]);

  // A draft resume (mode 'edit' here reaches Update, but the collection hasn't opened yet) is validated
  // the same as create: the collection open date must be today or in the future. No savedOpenDate is
  // passed — only CollectionEditor (an already scheduled/open collection) exempts its saved open date.
  const validateCurrent = (): Record<string, string> => (current === 'check' ? {} : validateCollectionSection(current, state));

  // A taken name only surfaces once the server checks it, so it needs the user routed back to the
  // details step to see it as an inline field error — the Name input doesn't render on any other step.
  const handleSubmitError = (error: unknown) => {
    dispatch({ type: 'BUMP_ATTEMPTS' });
    if (isCollectionNameTakenError(error)) {
      dispatch({ type: 'SET_ERRORS', errors: { name: 'Name must not match the name of another data collection' } });
      setStep(0);
      return;
    }
    dispatch({ type: 'SET_ERRORS', errors: { submit: mapCollectionError(error) } });
  };

  const createMutation = useMutation({
    mutationFn: createCollection,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: collectionsListKey });
      navigate('/admin/collections', {
        state: { banner: { title: 'Data collection added', body: `'${state.name}' is now on your list of data collections.` } },
      });
    },
    // BUMP_ATTEMPTS (inside handleSubmitError) drives GovUKErrorSummary's focusTrigger — without it a
    // server-side error reached via "Save and come back later" (which skips goNext) renders the summary
    // unfocused.
    onError: handleSubmitError,
  });

  const updateMutation = useMutation({
    mutationFn: updateCollection,
    onSuccess: () => {
      if (collectionId) {
        queryClient.invalidateQueries({ queryKey: collectionKey(collectionId) });
      }
      queryClient.invalidateQueries({ queryKey: collectionsListKey });
      navigate('/admin/collections', {
        state: { banner: { title: 'Edits saved', body: 'The data collection is now up to date.' } },
      });
    },
    onError: handleSubmitError,
  });

  const isSubmitting = createMutation.isPending || updateMutation.isPending;

  const submit = (saveAsDraft: boolean) => {
    if (mode === 'edit') {
      updateMutation.mutate({ ...buildUpdateInput(state, saveAsDraft, formVersionsEnabled), id: collectionId as string });
    } else {
      createMutation.mutate(buildCreateInput(state, saveAsDraft, formVersionsEnabled));
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
      navigate('/admin/collections');
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

  const errorList = buildCollectionErrorList(state, formOptions);

  const renderStep = (): React.ReactNode => {
    switch (current) {
      case 'details':
        return <StepDetails state={state} dispatch={dispatch} />;
      case 'window':
        return <StepWindow state={state} dispatch={dispatch} />;
      case 'forms':
        return <StepForms state={state} dispatch={dispatch} formOptions={formOptions} />;
      case 'reportingLas':
        return <StepReportingLas state={state} dispatch={dispatch} reportingLas={reportingLas} isLoading={reportingLasQuery.isLoading} />;
      case 'reminders':
        return <StepReminders state={state} dispatch={dispatch} />;
      default:
        return <StepCheckAnswers state={state} onChange={(target) => setStep(target)} formOptions={formOptions} reportingLas={reportingLas} />;
    }
  };

  const optionsLoading = formOptionsQuery.isLoading || reportingLasQuery.isLoading;
  const primaryLabel = isLastStep ? (mode === 'edit' ? 'Save changes' : 'Save data collection') : 'Continue';
  // The create flow can be saved as a draft on the data-entry steps, but its
  // final Check your answers screen has a single confirmation action. Keep the
  // secondary action in the edit flow, where users may still discard changes.
  const showSecondaryAction = allowSaveDraft && (mode === 'edit' || !isLastStep);

  const className = ['collection-wizard', current === 'window' ? 'collection-wizard--window' : '', current === 'reportingLas' ? 'collection-wizard--reporting-las' : '']
    .filter(Boolean)
    .join(' ');

  return (
    <div className={className}>
      <GovUKBackLink onClick={goBack} data-testid={`${TEST_ID}back`} />

      <GovUKErrorSummary errors={errorList} focusTrigger={state.submitAttempts} testId={`${TEST_ID}error-summary`} />

      <div className="collection-wizard__title-block">
        <span className="govuk-caption-l">{captionText}</span>
        <H1 className="collection-wizard__heading" id="collection-wizard-heading" tabIndex={-1}>
          {HEADINGS[current]}
        </H1>
      </div>

      {renderStep()}

      <div className="govuk-button-group">
        <GovUKButton onClick={handlePrimary} isLoading={isSubmitting} disabled={optionsLoading} data-testid={`${TEST_ID}continue`}>
          {primaryLabel}
        </GovUKButton>
        {showSecondaryAction && (
          <GovUKButton
            className="govuk-button govuk-button--secondary"
            onClick={
              mode === 'edit' && collectionId
                ? () => navigate(`/admin/collections/${collectionId}/edit/discard`, { state: { formState: state, step } satisfies CollectionWizardDiscardState })
                : () => submit(true)
            }
            isLoading={mode === 'create' ? isSubmitting : false}
            disabled={mode === 'create' ? false : isSubmitting}
            data-testid={mode === 'edit' ? `${TEST_ID}discard` : `${TEST_ID}save-draft`}>
            {mode === 'edit' ? 'Discard changes' : 'Save and come back later'}
          </GovUKButton>
        )}
      </div>
    </div>
  );
};

export default CollectionWizard;
