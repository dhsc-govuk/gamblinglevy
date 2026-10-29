import React, { useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams, Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import './styles.css';
import { GeneralLayout } from '../../../../layouts';
import {
  DeleteConfirmDialog,
  ErrorSummary,
  PageLoading,
  GovUKActionMenu,
  GovUKButton,
  GovUKFieldset,
  GovUKNotificationBanner,
  GovUKSummaryCard,
  GovUKTag,
  GovUKTableScroll,
  H1,
  H3,
  Paragraph,
  type ActionMenuItem,
  type SummaryCardAction,
  type TagColour,
} from '../../../../components/GovUKComponents';
import SectionForm from '../../../../components/Global/FormDesigner/SectionForm';
import ReorderControls from '../../../../components/Global/FormDesigner/ReorderControls';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle, { pageTitleWithName } from '../../../../hooks/usePageTitle';
import { formModuleKey, formModuleVersionsKey, formModulesKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import { composeTestId, slugifyTestId } from '../../../../helpers/testIds';
import { govukColors } from '../../../../styles/govuk-global';
import { ROUTES } from '../../../../helpers/routes';
import { canManageForms, formsBuilderBreadcrumbs } from '../navigation';
import { getFormModule, type DcFormQuestionDto, type DcFormSectionDto, type FormModuleStatus } from '../queries';
import {
  createSection,
  deleteQuestion,
  deleteSection,
  publishFormModule,
  reorderQuestions,
  reorderSections,
  updateFormModule,
  updateSection,
  startFormModuleDraft,
  type PublishFormModuleResponseDto,
  type SectionInput,
  type UpdateFormModuleInput,
} from '../mutations';
import { mapFormsError } from '../messages';

const STATUS_LABELS: Record<FormModuleStatus, string> = {
  draft: 'Draft',
  published: 'Published',
  archived: 'Archived',
};

const STATUS_COLOURS: Record<FormModuleStatus, TagColour> = {
  draft: 'yellow',
  published: 'green',
  archived: 'grey',
};

type SectionFormState = { mode: 'add' } | { mode: 'edit'; section: DcFormSectionDto } | null;

// What a publish did (BLUM-9102): saved a new version, or matched the last one because nothing changed. Both fields
// are null when the API saves no versions (BLUM_9087 off there).
const publishedMessage = ({ latestVersionNumber, versionCreated }: PublishFormModuleResponseDto): string => {
  if (latestVersionNumber === null) {
    return 'Form published successfully.';
  }
  return versionCreated
    ? `Form published as version ${latestVersionNumber}.`
    : `Form published. Nothing has changed since version ${latestVersionNumber}, so no new version was saved.`;
};

// Reorders an array of ids by swapping the item at `index` with its neighbour in the given direction.
const move = (ids: string[], index: number, direction: 'up' | 'down'): string[] => {
  const target = direction === 'up' ? index - 1 : index + 1;
  if (target < 0 || target >= ids.length) {
    return ids;
  }
  const next = [...ids];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
};

// Inline editor for the module's own details (name + description). Mirrors the SectionForm pattern.
const ModuleForm = ({
  initialName,
  initialDescription,
  isLoading,
  onSubmit,
  onCancel,
}: {
  initialName: string;
  initialDescription: string;
  isLoading: boolean;
  onSubmit: (input: UpdateFormModuleInput) => void;
  onCancel: () => void;
}): React.ReactElement => {
  const [name, setName] = useState(initialName);
  const [description, setDescription] = useState(initialDescription);
  const [error, setError] = useState<string | undefined>(undefined);
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const handleSubmit = () => {
    setSubmitAttempts((attempts) => attempts + 1);
    if (!name.trim()) {
      setError('Enter a form name');
      return;
    }
    setError(undefined);
    onSubmit({ name: name.trim(), description: description.trim() || null });
  };

  return (
    <div className="govuk-!-margin-bottom-6 govuk-!-padding-4" style={{ border: `1px solid ${govukColors.grey2}` }} data-testid="admin-forms-sections-module-form">
      <ErrorSummary heading="There is a problem" errors={error ? [{ targetName: 'admin-forms-sections-module-name', text: error }] : []} focusTrigger={submitAttempts} />

      <H3>Edit form details</H3>
      <GovUKFieldset.Input
        id="admin-forms-sections-module-name"
        name="admin-forms-sections-module-name"
        label="Form name"
        value={name}
        error={error}
        required
        onChange={(e) => setName(e.target.value)}
      />
      <GovUKFieldset.Textarea
        id="admin-forms-sections-module-description"
        name="admin-forms-sections-module-description"
        label="Description (optional)"
        rows={3}
        value={description}
        onChange={(e) => setDescription(e.target.value)}
      />
      <div className="govuk-button-group">
        <GovUKButton onClick={handleSubmit} disabled={isLoading} data-testid="admin-forms-sections-module-form-save">
          Save form details
        </GovUKButton>
        <button type="button" className="govuk-link" onClick={onCancel} data-testid="admin-forms-sections-module-form-cancel">
          Cancel
        </button>
      </div>
    </div>
  );
};

const FormsSections = (): React.ReactElement => {
  const { moduleId = '' } = useParams<{ moduleId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();

  const [sectionForm, setSectionForm] = useState<SectionFormState>(null);
  const [editingModule, setEditingModule] = useState(false);
  const [confirmSection, setConfirmSection] = useState<{ id: string; title: string } | null>(null);
  const [confirmQuestion, setConfirmQuestion] = useState<{ id: string; label: string } | null>(null);
  // A refused action, linked from the error summary to the control or field it concerns.
  const [actionError, setActionError] = useState<{ text: string; target: string } | null>(null);
  // A message handed over by the page that sent us here (restoring a version, BLUM-9102), shown once.
  const [actionSuccess, setActionSuccess] = useState<string | null>(() => (location.state as { banner?: string } | null)?.banner ?? null);
  // Bumped on every action attempt, so the summary takes focus each time a refusal arrives (BLUM-8760).
  const [actionAttempts, setActionAttempts] = useState(0);
  // 1 only after a publish or a new draft, whose button disappears with the status change: the banner takes focus then.
  // Also 1 after a delete: the deleted row, whose Actions toggle the dialog hands focus back to, goes with the refetch.
  // Also 1 when a restore handed over a banner: its confirm button is gone, so the banner takes focus.
  const [successFocus, setSuccessFocus] = useState(() => (actionSuccess ? 1 : 0));

  const beginAction = () => {
    setActionError(null);
    setActionSuccess(null);
    setActionAttempts((attempts) => attempts + 1);
    setSuccessFocus(0);
  };

  useEffect(() => {
    if (userRole && !canManageForms(userRole)) {
      navigate(ROUTES.admin.home);
    }
  }, [userRole, navigate]);

  const moduleQuery = useQuery({ queryKey: formModuleKey(moduleId), queryFn: () => getFormModule(moduleId), staleTime: defaultStaleTime, enabled: Boolean(moduleId) });
  const module = moduleQuery.data?.data;

  usePageTitle(`${actionError ? 'Error: ' : ''}${pageTitleWithName('Form', module?.name)}`);

  const sections = (module?.sections ?? []).slice().sort((a, b) => a.sectionNumber - b.sectionNumber);
  const questionsForSection = (sectionId: string): DcFormQuestionDto[] =>
    (module?.questions ?? [])
      .filter((question) => question.sectionId === sectionId)
      .slice()
      .sort((a, b) => a.displayOrder - b.displayOrder);

  // BLUM-9102: deleting a question a saved version published withdraws it — the row stays, inactive and outside any
  // section, so older answers keep their label. It is not the admin's to edit, so the builder does not list it; a
  // reorder still sends it last, so every question keeps a position of its own. The test mirrors the server's
  // FormModule.WithdrawQuestion (inactive, no section): change one and the other together.
  const isWithdrawn = (question: DcFormQuestionDto): boolean => !question.isActive && !question.sectionId;
  const withdrawnQuestionIds = (module?.questions ?? []).filter(isWithdrawn).map((q) => q.id);

  // Questions with no section, or pointing at a since-removed section, would otherwise be invisible.
  const unassignedQuestions = (module?.questions ?? [])
    .filter((q) => !isWithdrawn(q) && (!q.sectionId || !sections.some((s) => s.id === q.sectionId)))
    .slice()
    .sort((a, b) => a.displayOrder - b.displayOrder);

  const isDraft = module?.status === 'draft';

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: formModuleKey(moduleId) });
    queryClient.invalidateQueries({ queryKey: formModulesKey });
    queryClient.invalidateQueries({ queryKey: formModuleVersionsKey(moduleId) });
  };

  const updateModuleM = useMutation({
    mutationFn: (input: UpdateFormModuleInput) => updateFormModule(moduleId, input),
    onSuccess: () => {
      invalidate();
      setEditingModule(false);
      setActionSuccess('Form details updated successfully.');
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({ text: mapFormsError(error), target: 'admin-forms-sections-module-name' });
    },
  });
  const publishModuleM = useMutation({
    mutationFn: () => publishFormModule(moduleId),
    onSuccess: (response) => {
      invalidate();
      setActionSuccess(publishedMessage(response.data));
      setSuccessFocus(1);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({ text: mapFormsError(error), target: 'admin-forms-sections-publish' });
    },
  });
  const startDraftM = useMutation({
    mutationFn: () => startFormModuleDraft(moduleId),
    onSuccess: () => {
      invalidate();
      setActionSuccess('A new draft has been started. Publish it to save your changes as a new version.');
      setSuccessFocus(1);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({ text: mapFormsError(error), target: 'admin-forms-sections-start-draft' });
    },
  });
  const createSectionM = useMutation({
    mutationFn: (input: SectionInput) => createSection(moduleId, input),
    onSuccess: (_response, input) => {
      invalidate();
      setSectionForm(null);
      setActionSuccess(`${input.title} was added successfully.`);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({ text: mapFormsError(error), target: 'form-section-title' });
    },
  });
  const updateSectionM = useMutation({
    mutationFn: (vars: { id: string; input: SectionInput }) => updateSection(moduleId, vars.id, vars.input),
    onSuccess: (_response, variables) => {
      invalidate();
      setSectionForm(null);
      setActionSuccess(`${variables.input.title} was updated successfully.`);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({ text: mapFormsError(error), target: 'form-section-title' });
    },
  });
  const deleteSectionM = useMutation({
    mutationFn: (id: string) => deleteSection(moduleId, id),
    onSuccess: () => {
      invalidate();
      setConfirmSection(null);
      setActionSuccess('Section deleted successfully.');
      setSuccessFocus(1);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({ text: mapFormsError(error), target: 'admin-forms-sections-heading' });
      setConfirmSection(null);
    },
  });
  const deleteQuestionM = useMutation({
    mutationFn: (id: string) => deleteQuestion(moduleId, id),
    onSuccess: () => {
      invalidate();
      setConfirmQuestion(null);
      setActionSuccess('Question deleted successfully.');
      setSuccessFocus(1);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({ text: mapFormsError(error), target: 'admin-forms-sections-heading' });
      setConfirmQuestion(null);
    },
  });
  const reorderSectionsM = useMutation({
    mutationFn: (orderedIds: string[]) => reorderSections({ moduleId, orderedIds }),
    onSuccess: () => {
      invalidate();
      setActionSuccess('Section order updated successfully.');
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({ text: mapFormsError(error), target: 'admin-forms-sections-heading' });
    },
  });
  const reorderQuestionsM = useMutation({
    mutationFn: (orderedIds: string[]) => reorderQuestions({ moduleId, orderedIds }),
    onSuccess: () => {
      invalidate();
      setActionSuccess('Question order updated successfully.');
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({ text: mapFormsError(error), target: 'admin-forms-sections-heading' });
    },
  });

  const moveSection = (index: number, direction: 'up' | 'down') => {
    beginAction();
    reorderSectionsM.mutate(
      move(
        sections.map((s) => s.id),
        index,
        direction
      )
    );
  };
  // The reorder endpoint assigns DisplayOrder=index+1 across the WHOLE module, so we must always
  // send the full module-wide ordered id list (sections' questions in order, then unassigned, then withdrawn),
  // swapping only the affected group — otherwise other sections' questions collide.
  const moveQuestion = (sectionId: string, index: number, direction: 'up' | 'down') => {
    beginAction();
    const reordered = move(
      questionsForSection(sectionId).map((q) => q.id),
      index,
      direction
    );
    const globalOrder = [
      ...sections.flatMap((s) => (s.id === sectionId ? reordered : questionsForSection(s.id).map((q) => q.id))),
      ...unassignedQuestions.map((q) => q.id),
      ...withdrawnQuestionIds,
    ];
    reorderQuestionsM.mutate(globalOrder);
  };
  const moveQuestionInGroup = (index: number, direction: 'up' | 'down') => {
    beginAction();
    const reordered = move(
      unassignedQuestions.map((q) => q.id),
      index,
      direction
    );
    const globalOrder = [...sections.flatMap((s) => questionsForSection(s.id).map((q) => q.id)), ...reordered, ...withdrawnQuestionIds];
    reorderQuestionsM.mutate(globalOrder);
  };

  const sectionActions = (section: DcFormSectionDto): SummaryCardAction[] => {
    if (!isDraft) {
      return [];
    }
    return [
      {
        label: 'Edit',
        visuallyHiddenText: `section ${section.title}`,
        onClick: () => {
          beginAction();
          setSectionForm({ mode: 'edit', section });
        },
      },
      {
        label: 'Delete',
        visuallyHiddenText: `section ${section.title}`,
        onClick: () => {
          beginAction();
          setConfirmSection({ id: section.id, title: section.title });
        },
      },
    ];
  };

  return (
    <GeneralLayout
      breadcrumbs={formsBuilderBreadcrumbs(module ? [{ label: module.name, link: ROUTES.admin.forms.module(moduleId) }] : [])}
      backLink={{ href: ROUTES.admin.forms.list }}>
      <div className="govuk-!-padding-top-4">
        {moduleQuery.isLoading ? (
          <PageLoading />
        ) : !module ? (
          <Paragraph>This form could not be found.</Paragraph>
        ) : (
          <>
            <ErrorSummary
              errors={actionError ? [{ targetName: actionError.target, text: actionError.text }] : []}
              focusTrigger={actionAttempts}
              testId="admin-forms-sections-error"
            />
            <H1>
              {module.name}
              <span className="govuk-!-margin-left-2">
                <GovUKTag colour={STATUS_COLOURS[module.status]}>{STATUS_LABELS[module.status]}</GovUKTag>
              </span>
            </H1>
            {module.description && <Paragraph>{module.description}</Paragraph>}
            <Paragraph>
              <span className="govuk-hint govuk-!-margin-bottom-0">Form code: {module.code}</span>
            </Paragraph>
            <Paragraph>
              <RouterLink to={ROUTES.admin.forms.versions(moduleId)} className="govuk-link" data-testid="admin-forms-sections-versions">
                View previous versions
              </RouterLink>
            </Paragraph>

            {actionSuccess && (
              <GovUKNotificationBanner type="success" title="Form updated" focusTrigger={successFocus} testId="admin-forms-sections-success">
                <Paragraph>{actionSuccess}</Paragraph>
              </GovUKNotificationBanner>
            )}

            {module.status === 'published' && (
              <>
                <div className="govuk-inset-text" data-testid="admin-forms-sections-locked">
                  This form is published and cannot be changed. To change it, start a new draft. When you publish the draft, it is saved as a new version.
                </div>
                <div className="govuk-button-group">
                  <GovUKButton
                    className="govuk-button govuk-button--secondary"
                    onClick={() => {
                      beginAction();
                      startDraftM.mutate();
                    }}
                    disabled={startDraftM.isPending}
                    id="admin-forms-sections-start-draft"
                    data-testid="admin-forms-sections-start-draft">
                    Start a new draft
                  </GovUKButton>
                </div>
              </>
            )}

            {module.status === 'archived' && (
              <div className="govuk-inset-text" data-testid="admin-forms-sections-archived">
                This form is archived and cannot be changed.
              </div>
            )}

            {isDraft && editingModule && (
              <ModuleForm
                initialName={module.name}
                initialDescription={module.description ?? ''}
                isLoading={updateModuleM.isPending}
                onSubmit={(input) => {
                  beginAction();
                  updateModuleM.mutate(input);
                }}
                onCancel={() => {
                  setActionError(null);
                  setEditingModule(false);
                }}
              />
            )}

            {isDraft && !editingModule && !sectionForm && (
              <div className="govuk-button-group">
                <GovUKButton
                  onClick={() => {
                    beginAction();
                    publishModuleM.mutate();
                  }}
                  disabled={publishModuleM.isPending}
                  id="admin-forms-sections-publish"
                  data-testid="admin-forms-sections-publish">
                  Publish form
                </GovUKButton>
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={() => {
                    beginAction();
                    setEditingModule(true);
                  }}
                  data-testid="admin-forms-sections-edit-details">
                  Edit form details
                </GovUKButton>
              </div>
            )}

            <H3 id="admin-forms-sections-heading" tabIndex={-1}>
              Sections
            </H3>

            {isDraft && sectionForm?.mode === 'add' && (
              <SectionForm
                heading="Add a section"
                submitLabel="Add section"
                isLoading={createSectionM.isPending}
                onSubmit={(input) => {
                  beginAction();
                  createSectionM.mutate({ title: input.title, description: input.description, helpText: null });
                }}
                onCancel={() => {
                  setActionError(null);
                  setSectionForm(null);
                }}
              />
            )}

            {isDraft && !sectionForm && !editingModule && (
              <div className="govuk-button-group">
                <GovUKButton
                  onClick={() => {
                    beginAction();
                    setSectionForm({ mode: 'add' });
                  }}
                  data-testid="admin-forms-sections-add-section">
                  Add section
                </GovUKButton>
                <RouterLink to={ROUTES.admin.forms.createQuestion(moduleId)} className="govuk-link" data-testid="admin-forms-sections-add-question">
                  Add a question
                </RouterLink>
              </div>
            )}

            {sections.length === 0 && !sectionForm && <Paragraph>No sections yet. Add a section to start building the form.</Paragraph>}

            {sections.map((section, sectionIndex) => {
              // Keyed on the id, not the admin-typed title, so a rename or two equal titles cannot change or clash it.
              const sectionTestId = composeTestId('admin-forms-sections-section', section.id);
              const questions = questionsForSection(section.id);

              if (isDraft && sectionForm?.mode === 'edit' && sectionForm.section.id === section.id) {
                return (
                  <SectionForm
                    key={section.id}
                    heading="Edit section"
                    submitLabel="Save section"
                    initialTitle={section.title}
                    initialDescription={section.description ?? ''}
                    isLoading={updateSectionM.isPending}
                    onSubmit={(input) => {
                      beginAction();
                      updateSectionM.mutate({ id: section.id, input: { title: input.title, description: input.description, helpText: section.helpText } });
                    }}
                    onCancel={() => {
                      setActionError(null);
                      setSectionForm(null);
                    }}
                  />
                );
              }

              return (
                <GovUKSummaryCard
                  key={section.id}
                  testId={sectionTestId}
                  title={
                    <>
                      {section.title}
                      {!section.isActive && (
                        <span className="govuk-!-margin-left-2">
                          <GovUKTag colour="grey">Hidden</GovUKTag>
                        </span>
                      )}
                    </>
                  }
                  actions={sectionActions(section)}>
                  {section.description && <Paragraph>{section.description}</Paragraph>}

                  {isDraft && (
                    <div className="govuk-!-margin-bottom-3">
                      <ReorderControls
                        label={`section ${section.title}`}
                        isFirst={sectionIndex === 0}
                        isLast={sectionIndex === sections.length - 1}
                        onMoveUp={() => moveSection(sectionIndex, 'up')}
                        onMoveDown={() => moveSection(sectionIndex, 'down')}
                        testId={composeTestId(sectionTestId, 'section')}
                      />
                    </div>
                  )}

                  {questions.length === 0 ? (
                    <Paragraph>No questions in this section yet.</Paragraph>
                  ) : (
                    <GovUKTableScroll label={`${section.title} questions`} testId={composeTestId(sectionTestId, 'questions', 'scroll')}>
                      <table className="govuk-table forms-sections-questions-table" data-testid={composeTestId(sectionTestId, 'questions')}>
                        <thead className="govuk-table__head">
                          <tr className="govuk-table__row">
                            <th scope="col" className="govuk-table__header">
                              Question
                            </th>
                            <th scope="col" className="govuk-table__header">
                              Type
                            </th>
                            <th scope="col" className="govuk-table__header">
                              Code
                            </th>
                            <th scope="col" className="govuk-table__header">
                              <span className="govuk-visually-hidden">Actions</span>
                            </th>
                          </tr>
                        </thead>
                        <tbody className="govuk-table__body">
                          {questions.map((question, questionIndex) => {
                            const questionTestId = composeTestId(sectionTestId, 'question', slugifyTestId(question.code) || question.id);
                            const questionActions: ActionMenuItem[] = [
                              ...(questionIndex > 0
                                ? [
                                    {
                                      label: 'Move up',
                                      onClick: () => moveQuestion(section.id, questionIndex, 'up'),
                                      testId: composeTestId(questionTestId, 'move-up'),
                                    },
                                  ]
                                : []),
                              ...(questionIndex < questions.length - 1
                                ? [
                                    {
                                      label: 'Move down',
                                      onClick: () => moveQuestion(section.id, questionIndex, 'down'),
                                      testId: composeTestId(questionTestId, 'move-down'),
                                    },
                                  ]
                                : []),
                              {
                                label: 'Edit',
                                href: ROUTES.admin.forms.editQuestion(moduleId, question.id),
                                testId: composeTestId(questionTestId, 'edit'),
                              },
                              {
                                label: 'Delete',
                                onClick: () => {
                                  beginAction();
                                  setConfirmQuestion({ id: question.id, label: question.label });
                                },
                                testId: composeTestId(questionTestId, 'delete'),
                              },
                            ];
                            return (
                              <tr className="govuk-table__row" key={question.id} data-testid={questionTestId}>
                                <td className="govuk-table__cell">
                                  {question.label}
                                  {question.isRequired && (
                                    <span className="govuk-!-margin-left-1">
                                      <GovUKTag colour="purple">Required</GovUKTag>
                                    </span>
                                  )}
                                  {!question.isActive && (
                                    <span className="govuk-!-margin-left-1">
                                      <GovUKTag colour="grey">Hidden</GovUKTag>
                                    </span>
                                  )}
                                </td>
                                <td className="govuk-table__cell">
                                  <GovUKTag colour="blue">{question.questionType}</GovUKTag>
                                </td>
                                <td className="govuk-table__cell">
                                  <span className="govuk-hint govuk-!-margin-bottom-0">{question.code}</span>
                                </td>
                                <td className="govuk-table__cell govuk-table__cell--numeric">
                                  {isDraft ? (
                                    <GovUKActionMenu items={questionActions} buttonLabel="Actions" testId={composeTestId(questionTestId, 'actions')} />
                                  ) : (
                                    <span className="govuk-visually-hidden">No actions available</span>
                                  )}
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    </GovUKTableScroll>
                  )}

                  {isDraft && (
                    <RouterLink
                      to={`${ROUTES.admin.forms.createQuestion(moduleId)}?sectionId=${section.id}`}
                      className="govuk-link"
                      data-testid={composeTestId(sectionTestId, 'add-question')}>
                      Add a question to this section
                    </RouterLink>
                  )}
                </GovUKSummaryCard>
              );
            })}

            {unassignedQuestions.length > 0 && (
              <GovUKSummaryCard testId="admin-forms-sections-unassigned" title="Questions not in a section">
                <Paragraph>These questions are not assigned to a section. Edit each one to move it into a section.</Paragraph>
                <GovUKTableScroll label="Questions not in a section" testId="admin-forms-sections-unassigned-questions-scroll">
                  <table className="govuk-table forms-sections-questions-table" data-testid="admin-forms-sections-unassigned-questions">
                    <thead className="govuk-table__head">
                      <tr className="govuk-table__row">
                        <th scope="col" className="govuk-table__header">
                          Question
                        </th>
                        <th scope="col" className="govuk-table__header">
                          Type
                        </th>
                        <th scope="col" className="govuk-table__header">
                          Code
                        </th>
                        <th scope="col" className="govuk-table__header">
                          <span className="govuk-visually-hidden">Actions</span>
                        </th>
                      </tr>
                    </thead>
                    <tbody className="govuk-table__body">
                      {unassignedQuestions.map((question, questionIndex) => {
                        const questionTestId = composeTestId('admin-forms-sections-unassigned-question', slugifyTestId(question.code) || question.id);
                        const questionActions: ActionMenuItem[] = [
                          ...(questionIndex > 0
                            ? [
                                {
                                  label: 'Move up',
                                  onClick: () => moveQuestionInGroup(questionIndex, 'up'),
                                  testId: composeTestId(questionTestId, 'move-up'),
                                },
                              ]
                            : []),
                          ...(questionIndex < unassignedQuestions.length - 1
                            ? [
                                {
                                  label: 'Move down',
                                  onClick: () => moveQuestionInGroup(questionIndex, 'down'),
                                  testId: composeTestId(questionTestId, 'move-down'),
                                },
                              ]
                            : []),
                          {
                            label: 'Edit',
                            href: ROUTES.admin.forms.editQuestion(moduleId, question.id),
                            testId: composeTestId(questionTestId, 'edit'),
                          },
                          {
                            label: 'Delete',
                            onClick: () => {
                              beginAction();
                              setConfirmQuestion({ id: question.id, label: question.label });
                            },
                            testId: composeTestId(questionTestId, 'delete'),
                          },
                        ];
                        return (
                          <tr className="govuk-table__row" key={question.id} data-testid={questionTestId}>
                            <td className="govuk-table__cell">
                              {question.label}
                              {question.isRequired && (
                                <span className="govuk-!-margin-left-1">
                                  <GovUKTag colour="purple">Required</GovUKTag>
                                </span>
                              )}
                              {!question.isActive && (
                                <span className="govuk-!-margin-left-1">
                                  <GovUKTag colour="grey">Hidden</GovUKTag>
                                </span>
                              )}
                            </td>
                            <td className="govuk-table__cell">
                              <GovUKTag colour="blue">{question.questionType}</GovUKTag>
                            </td>
                            <td className="govuk-table__cell">
                              <span className="govuk-hint govuk-!-margin-bottom-0">{question.code}</span>
                            </td>
                            <td className="govuk-table__cell govuk-table__cell--numeric">
                              {isDraft ? (
                                <GovUKActionMenu items={questionActions} buttonLabel="Actions" testId={composeTestId(questionTestId, 'actions')} />
                              ) : (
                                <span className="govuk-visually-hidden">No actions available</span>
                              )}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </GovUKTableScroll>
              </GovUKSummaryCard>
            )}
          </>
        )}

        {confirmSection && (
          <DeleteConfirmDialog
            testId="admin-forms-sections-section-delete"
            open
            title="Delete this section?"
            message="A section can only be deleted when it has no questions."
            itemName={confirmSection.title}
            confirmLabel="Delete section"
            onCancel={() => setConfirmSection(null)}
            onConfirm={() => deleteSectionM.mutate(confirmSection.id)}
            isLoading={deleteSectionM.isPending}
          />
        )}

        {confirmQuestion && (
          <DeleteConfirmDialog
            testId="admin-forms-sections-question-delete"
            open
            title="Delete this question?"
            message="Saved answers are kept, but the question will no longer appear on the form."
            itemName={confirmQuestion.label}
            confirmLabel="Delete question"
            onCancel={() => setConfirmQuestion(null)}
            onConfirm={() => deleteQuestionM.mutate(confirmQuestion.id)}
            isLoading={deleteQuestionM.isPending}
          />
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormsSections;
