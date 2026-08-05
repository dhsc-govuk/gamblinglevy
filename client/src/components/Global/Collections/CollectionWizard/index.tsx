import React, { useLayoutEffect, useMemo, useReducer, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
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
  datePartsValidationIssue,
  initialCollectionFormState,
  type CollectionFormState,
} from '../../../../views/Admin/Collections/reducer';
import { getCollectionFormOptions, getReportingLas } from '../../../../views/Admin/Collections/queries';
import { createCollection, updateCollection } from '../../../../views/Admin/Collections/mutations';
import { mapCollectionError } from '../../../../views/Admin/Collections/messages';
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

  const formOptionsQuery = useQuery(collectionFormOptionsKey, getCollectionFormOptions, { staleTime: defaultStaleTime });
  const formOptions = formOptionsQuery.data?.data ?? [];
  const reportingLasQuery = useQuery(collectionReportingLasKey, getReportingLas, { staleTime: defaultStaleTime });
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

  const validateCurrent = (): Record<string, string> => {
    const errors: Record<string, string> = {};
    const validateDate = (key: 'open-date' | 'deadline-date', day: string, month: string, year: string, missingMessage: string) => {
      const issue = datePartsValidationIssue(day, month, year);
      if (!issue) {
        return;
      }
      const issueMessages = {
        missing: missingMessage,
        day: 'Enter a valid day',
        month: 'Enter a valid month',
        year: 'Enter a valid year',
      };
      errors[issue === 'missing' ? key : `${key}-${issue}`] = issueMessages[issue];
    };

    switch (current) {
      case 'details': {
        if (!state.name.trim()) {
          errors['name'] = 'Enter a name for this data collection';
        }
        if (!state.type) {
          errors['type'] = 'Select a type';
        }
        if (state.type && !state.financialYear) {
          errors['financialYear'] = 'Select a financial year';
        }
        if (state.type === 'quarterly_report' && !state.quarter) {
          errors['quarter'] = 'Select a quarter';
        }
        return errors;
      }
      case 'window': {
        if (!state.windowSameAsReportingPeriod) {
          errors['window-same'] = 'Select an option';
        }
        if (state.windowSameAsReportingPeriod === 'no') {
          validateDate('open-date', state.opensDay, state.opensMonth, state.opensYear, 'Enter when the data collection will open');
          validateDate('deadline-date', state.deadlineDay, state.deadlineMonth, state.deadlineYear, 'Enter the deadline for submissions');
        }
        return errors;
      }
      case 'forms': {
        if (state.formModuleIds.length === 0) {
          errors['forms'] = 'Select at least one form';
        }
        return errors;
      }
      case 'reportingLas': {
        if (!state.isSubmittedByAllLas) {
          errors['all-las'] = 'Select an option';
        }
        if (state.isSubmittedByAllLas === 'no' && state.selectedLaIds.length === 0) {
          errors['selected-las'] = 'Select at least one organisation';
        }
        return errors;
      }
      case 'reminders': {
        state.reminders.forEach((reminder, index) => {
          if (!reminder.offsetType) {
            errors[`reminder-${index}-trigger`] = 'Select when to send the reminder';
          }
          if (reminder.offsetType && !reminder.days.trim()) {
            errors[`reminder-${index}-days`] = 'Enter the number of days';
          }
        });
        return errors;
      }
      default:
        return errors;
    }
  };

  const createMutation = useMutation(createCollection, {
    onSuccess: () => {
      queryClient.invalidateQueries(collectionsListKey);
      navigate('/admin/collections', {
        state: { banner: { title: 'Edits saved', body: `'${state.name}' is now on your list of data collections.` } },
      });
    },
    onError: (error) => {
      // BUMP_ATTEMPTS drives GovUKErrorSummary's focusTrigger — without it a server-side error reached
      // via "Save and come back later" (which skips goNext) renders the summary unfocused.
      dispatch({ type: 'BUMP_ATTEMPTS' });
      dispatch({ type: 'SET_ERRORS', errors: { submit: mapCollectionError(error) } });
    },
  });

  const updateMutation = useMutation(updateCollection, {
    onSuccess: () => {
      if (collectionId) {
        queryClient.invalidateQueries(collectionKey(collectionId));
      }
      queryClient.invalidateQueries(collectionsListKey);
      navigate('/admin/collections', {
        state: { banner: { title: 'Edits saved', body: 'The data collection is now up to date.' } },
      });
    },
    onError: (error) => {
      dispatch({ type: 'BUMP_ATTEMPTS' });
      dispatch({ type: 'SET_ERRORS', errors: { submit: mapCollectionError(error) } });
    },
  });

  const isSubmitting = createMutation.isLoading || updateMutation.isLoading;

  const submit = (saveAsDraft: boolean) => {
    if (mode === 'edit') {
      updateMutation.mutate({ ...buildUpdateInput(state, saveAsDraft), id: collectionId as string });
    } else {
      createMutation.mutate(buildCreateInput(state, saveAsDraft));
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
