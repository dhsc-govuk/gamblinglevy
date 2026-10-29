import React, { useLayoutEffect, useMemo, useReducer, useState } from 'react';
import useFeature from '../../../../hooks/useFeature';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout, ManagementLayout } from '../../../../layouts';
import { GovUKBackLink, GovUKButton, GovUKErrorSummary, H1 } from '../../../GovUKComponents';
import StepDetails from '../StepDetails';
import StepWindow from '../StepWindow';
import StepForms from '../StepForms';
import StepReportingLas from '../StepReportingLas';
import StepReminders from '../StepReminders';
import StepCheckAnswers from '../StepCheckAnswers';
import ConfirmationPanel from '../../Activities/ConfirmationPanel';
import usePageTitle from '../../../../hooks/usePageTitle';
import scrollToTop from '../../../../helpers/scrollToTop';
import { collectionFormOptionsKey, collectionKey, collectionReportingLasKey, collectionsListKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import {
  buildCollectionErrorList,
  buildUpdateInput,
  collectionFormReducer,
  formOptionsForType,
  hydrateCollectionState,
  initialCollectionFormState,
  validateCollectionSection,
} from '../../../../views/Admin/Collections/reducer';
import { getCollectionFormOptions, getReportingLas, type CollectionDto } from '../../../../views/Admin/Collections/queries';
import { updateCollection } from '../../../../views/Admin/Collections/mutations';
import { isCollectionNameTakenError, mapCollectionError } from '../../../../views/Admin/Collections/messages';
import { useDefaultCollectionForms } from '../useDefaultCollectionForms';
import './styles.css';

type SectionKey = 'details' | 'window' | 'forms' | 'reportingLas' | 'reminders';
type Mode = 'check' | SectionKey;

interface Props {
  collection: CollectionDto;
}

// Section headings (mirror the wizard's per-step headings) shown when editing a single section.
const HEADINGS: Record<SectionKey, string> = {
  details: 'Data collection details',
  window: 'Collection window',
  forms: 'Forms',
  reportingLas: 'Reporting organisations',
  reminders: 'Automatic reminders',
};

// StepCheckAnswers' "Change" links emit a numeric step index (0 details … 4 reminders). Map those to
// the editor's section-key modes so the same check-answers component drives this editor.
const STEP_TO_SECTION: SectionKey[] = ['details', 'window', 'forms', 'reportingLas', 'reminders'];

// `?edit=` deep-link values. The View page links use `reporting` for the reporting-LAs card, so accept
// both that and the canonical `reportingLas` key.
const SECTION_FROM_PARAM: Record<string, SectionKey> = {
  details: 'details',
  window: 'window',
  forms: 'forms',
  reporting: 'reportingLas',
  reportingLas: 'reportingLas',
  reminders: 'reminders',
};

const TEST_ID = 'admin-collections-edit-';

// "Change data collection" check-your-answers editor for completed (open/scheduled) collections. Edits
// one section at a time then commits with Save changes / Discard changes (mirrors ActivityEditor). The
// summary uses the same StepCheckAnswers component as the create wizard.
const CollectionEditor = ({ collection }: Props): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [searchParams] = useSearchParams();

  const [state, dispatch] = useReducer(collectionFormReducer, { ...initialCollectionFormState, ...hydrateCollectionState(collection) });
  // BLUM-9103: the per-form version choice is offered, and so sent, only while form versioning is on.
  const formVersionsEnabled = useFeature('BLUM_9087');

  const formOptionsQuery = useQuery({ queryKey: collectionFormOptionsKey, queryFn: getCollectionFormOptions, staleTime: defaultStaleTime });
  // Only the collection type's forms are offered (BLUM-7943). The saved selection is kept; a type change here
  // starts the new type with every form ticked, as in the create wizard.
  const formOptions = useMemo(() => formOptionsForType(formOptionsQuery.data?.data ?? [], state.type), [formOptionsQuery.data, state.type]);
  useDefaultCollectionForms(state, dispatch, formOptions, formOptionsQuery.isSuccess);
  const reportingLasQuery = useQuery({ queryKey: collectionReportingLasKey, queryFn: getReportingLas, staleTime: defaultStaleTime });
  const reportingLas = reportingLasQuery.data?.data ?? [];

  // The `?edit=` deep link is applied once, by the state initialiser below. The collection is already loaded when
  // this component mounts, so unlike ActivityEditor there is nothing to wait for and re-resolve.
  const resolveInitial = (): Mode => {
    const param = searchParams.get('edit');
    if (!param) {
      return 'check';
    }
    return SECTION_FROM_PARAM[param] ?? 'check';
  };

  const [mode, setMode] = useState<Mode>(resolveInitial);
  const [discarding, setDiscarding] = useState(false);

  const hasErrors = Object.keys(state.errors).length > 0;
  const currentPageTitle = discarding ? 'Discard changes' : mode === 'check' ? 'Check your answers' : HEADINGS[mode];
  usePageTitle(`${hasErrors && state.submitAttempts > 0 && !discarding ? 'Error: ' : ''}${currentPageTitle}`);

  // Editing a section, returning to check answers, and entering/leaving the discard interruption are all
  // in-page view changes on a single route. Reset the visual position and move focus to the new heading so
  // keyboard/screen-reader users start from the top of the replacement content, rather than focus lingering
  // on the now-changed Continue button. While discarding, the heading is unmounted (the ConfirmationPanel is
  // shown and self-focuses its own heading), so getElementById no-ops here; on cancel it regains focus.
  useLayoutEffect(() => {
    scrollToTop();
    document.getElementById('collection-wizard-heading')?.focus({ preventScroll: true });
  }, [mode, discarding]);

  // Per-section validation, shared with the create wizard. This collection is already scheduled/open, so
  // its saved open date may legitimately be in the past by now — that one date is exempt from the
  // today-or-future check, but any other past open date or deadline is refused (BLUM-8161).
  const validateSection = (section: SectionKey): Record<string, string> => validateCollectionSection(section, state, { savedOpenDate: collection.opensDate });

  const updateMutation = useMutation({
    mutationFn: updateCollection,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: collectionsListKey });
      queryClient.invalidateQueries({ queryKey: collectionKey(collection.id) });
      navigate(`/admin/collections/${collection.id}`, {
        state: { banner: { title: 'Edits saved', body: 'The data collection is now up to date.' } },
      });
    },
    // A taken name only surfaces once the server checks it, so it needs the user routed back to the
    // details section to see it as an inline field error — the Name input doesn't render on 'check'.
    onError: (error) => {
      dispatch({ type: 'BUMP_ATTEMPTS' });
      if (isCollectionNameTakenError(error)) {
        dispatch({ type: 'SET_ERRORS', errors: { name: 'Name must not match the name of another data collection' } });
        setMode('details');
        return;
      }
      dispatch({ type: 'SET_ERRORS', errors: { submit: mapCollectionError(error) } });
    },
  });

  // Validate a single section and return to the summary when it passes.
  const continueFromSection = (section: SectionKey) => {
    const errors = validateSection(section);
    dispatch({ type: 'BUMP_ATTEMPTS' });
    dispatch({ type: 'SET_ERRORS', errors });
    if (Object.keys(errors).length === 0) {
      setMode('check');
    }
  };

  // Validate every section before committing; jump to the first one with a problem. `saveAsDraft`
  // is threaded through to the backend — for already-published collections the backend ignores it
  // (Publish only runs on draft→non-draft transitions; nothing in UpdateCollection demotes a
  // scheduled/open record back to draft), so it's a no-op on non-drafts. We still validate strictly.
  const commit = (saveAsDraft: boolean) => {
    for (const section of STEP_TO_SECTION) {
      const errors = validateSection(section);
      if (Object.keys(errors).length > 0) {
        dispatch({ type: 'BUMP_ATTEMPTS' });
        dispatch({ type: 'SET_ERRORS', errors });
        setMode(section);
        return;
      }
    }
    dispatch({ type: 'SET_ERRORS', errors: {} });
    updateMutation.mutate({ ...buildUpdateInput(state, saveAsDraft, formVersionsEnabled), id: collection.id });
  };

  const handleSave = () => commit(false);

  const errorList = buildCollectionErrorList(state, formOptions);

  const renderSection = (section: SectionKey): React.ReactNode => {
    switch (section) {
      case 'details':
        return <StepDetails state={state} dispatch={dispatch} />;
      case 'window':
        return <StepWindow state={state} dispatch={dispatch} />;
      case 'forms':
        return <StepForms state={state} dispatch={dispatch} formOptions={formOptions} />;
      case 'reportingLas':
        return <StepReportingLas state={state} dispatch={dispatch} reportingLas={reportingLas} isLoading={reportingLasQuery.isLoading} />;
      default:
        return <StepReminders state={state} dispatch={dispatch} />;
    }
  };

  const optionsLoading = formOptionsQuery.isLoading || reportingLasQuery.isLoading;

  if (discarding) {
    return (
      <GeneralLayout>
        <div style={{ paddingTop: '20px' }}>
          <GovUKBackLink onClick={() => setDiscarding(false)} data-testid={`${TEST_ID}discard-back`} />
          <ConfirmationPanel
            testId="admin-collections-edit-discard"
            title="Are you sure you want to discard changes?"
            body={
              <>
                <p className="govuk-body" style={{ color: '#ffffff' }}>
                  Discarding changes to the data collection &lsquo;{collection.name}&rsquo; will remove any edits you made and keep the original information.
                </p>
                <p className="govuk-body" style={{ color: '#ffffff' }}>
                  This cannot be undone.
                </p>
                <p className="govuk-body" style={{ color: '#ffffff' }}>
                  If you want to keep your changes, you can go back and save them.
                </p>
              </>
            }
            confirmLabel="Discard changes"
            onConfirm={() =>
              navigate(`/admin/collections/${collection.id}`, {
                state: { banner: { title: 'Changes discarded', body: 'The data collection was not edited.' } },
              })
            }
            cancelLabel="Go back"
            onCancel={() => setDiscarding(false)}
          />
        </div>
      </GeneralLayout>
    );
  }

  return (
    <ManagementLayout>
      <div style={{ paddingTop: '20px' }}>
        <GovUKBackLink onClick={() => (mode === 'check' ? navigate(`/admin/collections/${collection.id}`) : setMode('check'))} data-testid={`${TEST_ID}back`} />

        <GovUKErrorSummary errors={errorList} focusTrigger={state.submitAttempts} testId={`${TEST_ID}error-summary`} />

        <span className="govuk-caption-l">Change data collection</span>

        {mode === 'check' ? (
          <>
            <H1 className="collection-editor__heading" id="collection-wizard-heading" tabIndex={-1}>
              Check your answers
            </H1>
            <StepCheckAnswers state={state} formOptions={formOptions} reportingLas={reportingLas} onChange={(stepIndex) => setMode(STEP_TO_SECTION[stepIndex])} />
            <div className="govuk-button-group">
              <GovUKButton onClick={handleSave} isLoading={updateMutation.isPending} disabled={optionsLoading} data-testid={`${TEST_ID}save`}>
                Save changes
              </GovUKButton>
              <GovUKButton className="govuk-button govuk-button--secondary" onClick={() => setDiscarding(true)} data-testid={`${TEST_ID}discard`}>
                Discard changes
              </GovUKButton>
            </div>
          </>
        ) : (
          <>
            <H1 className="collection-editor__heading" id="collection-wizard-heading" tabIndex={-1}>
              {HEADINGS[mode]}
            </H1>
            {renderSection(mode)}
            <div className="govuk-button-group">
              <GovUKButton onClick={() => continueFromSection(mode)} disabled={optionsLoading || updateMutation.isPending} data-testid={`${TEST_ID}continue`}>
                Continue
              </GovUKButton>
              <GovUKButton className="govuk-button govuk-button--secondary" onClick={() => setDiscarding(true)} data-testid={`${TEST_ID}discard`}>
                Discard changes
              </GovUKButton>
            </div>
          </>
        )}
      </div>
    </ManagementLayout>
  );
};

export default CollectionEditor;
