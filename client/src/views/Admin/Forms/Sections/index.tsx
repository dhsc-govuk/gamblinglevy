import React, { useEffect, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, useParams, Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import './styles.css';
import { GeneralLayout } from '../../../../layouts';
import {
  DeleteConfirmDialog,
  ErrorSummary,
  GovUKActionMenu,
  GovUKButton,
  GovUKFieldset,
  GovUKNotificationBanner,
  GovUKSummaryCard,
  GovUKTag,
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
import usePageTitle from '../../../../hooks/usePageTitle';
import { formModuleKey, formModulesKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import { slugifyTestId } from '../../../../helpers/testIds';
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
    <div className="govuk-!-margin-bottom-6 govuk-!-padding-4" style={{ border: '1px solid #b1b4b6' }} data-testid="admin-forms-sections-module-form">
      <ErrorSummary
        heading="There is a problem"
        errors={error ? [{ targetName: 'admin-forms-sections-module-name', text: error }] : []}
        focusTrigger={submitAttempts}
      />

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
        <button
          type="button"
          className="govuk-link"
          style={{ background: 'none', border: 0, padding: 0, cursor: 'pointer' }}
          onClick={onCancel}
          data-testid="admin-forms-sections-module-form-cancel">
          Cancel
        </button>
      </div>
    </div>
  );
};

const FormsSections = (): React.ReactElement => {
  const { moduleId = '' } = useParams<{ moduleId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();

  const [sectionForm, setSectionForm] = useState<SectionFormState>(null);
  const [editingModule, setEditingModule] = useState(false);
  const [confirmSection, setConfirmSection] = useState<{ id: string; title: string } | null>(null);
  const [confirmQuestion, setConfirmQuestion] = useState<{ id: string; label: string } | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);

  useEffect(() => {
    if (userRole && !canManageForms(userRole)) {
      navigate('/admin/home');
    }
  }, [userRole, navigate]);

  const moduleQuery = useQuery(formModuleKey(moduleId), () => getFormModule(moduleId), {
    staleTime: defaultStaleTime,
    enabled: Boolean(moduleId),
  });
  const module = moduleQuery.data?.data;

  usePageTitle(module ? module.name : 'Form');

  const sections = (module?.sections ?? []).slice().sort((a, b) => a.sectionNumber - b.sectionNumber);
  const questionsForSection = (sectionId: string): DcFormQuestionDto[] =>
    (module?.questions ?? [])
      .filter((question) => question.sectionId === sectionId)
      .slice()
      .sort((a, b) => a.displayOrder - b.displayOrder);

  // Questions with no section, or pointing at a since-removed section, would otherwise be invisible.
  const unassignedQuestions = (module?.questions ?? [])
    .filter((q) => !q.sectionId || !sections.some((s) => s.id === q.sectionId))
    .slice()
    .sort((a, b) => a.displayOrder - b.displayOrder);

  const isDraft = module?.status === 'draft';

  const invalidate = () => {
    queryClient.invalidateQueries(formModuleKey(moduleId));
    queryClient.invalidateQueries(formModulesKey);
  };

  const updateModuleM = useMutation((input: UpdateFormModuleInput) => updateFormModule(moduleId, input), {
    onSuccess: () => {
      invalidate();
      setEditingModule(false);
      setActionSuccess('Form details updated successfully.');
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError(mapFormsError(error));
    },
  });
  const publishModuleM = useMutation(() => publishFormModule(moduleId), {
    onSuccess: () => {
      invalidate();
      setActionSuccess('Form published successfully.');
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError(mapFormsError(error));
    },
  });
  const createSectionM = useMutation((input: SectionInput) => createSection(moduleId, input), {
    onSuccess: (_response, input) => {
      invalidate();
      setSectionForm(null);
      setActionSuccess(`${input.title} was added successfully.`);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError(mapFormsError(error));
    },
  });
  const updateSectionM = useMutation((vars: { id: string; input: SectionInput }) => updateSection(moduleId, vars.id, vars.input), {
    onSuccess: (_response, variables) => {
      invalidate();
      setSectionForm(null);
      setActionSuccess(`${variables.input.title} was updated successfully.`);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError(mapFormsError(error));
    },
  });
  const deleteSectionM = useMutation((id: string) => deleteSection(moduleId, id), {
    onSuccess: () => {
      invalidate();
      setConfirmSection(null);
      setActionSuccess('Section deleted successfully.');
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError(mapFormsError(error));
      setConfirmSection(null);
    },
  });
  const deleteQuestionM = useMutation((id: string) => deleteQuestion(moduleId, id), {
    onSuccess: () => {
      invalidate();
      setConfirmQuestion(null);
      setActionSuccess('Question deleted successfully.');
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError(mapFormsError(error));
      setConfirmQuestion(null);
    },
  });
  const reorderSectionsM = useMutation((orderedIds: string[]) => reorderSections({ moduleId, orderedIds }), {
    onSuccess: () => {
      invalidate();
      setActionSuccess('Section order updated successfully.');
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError(mapFormsError(error));
    },
  });
  const reorderQuestionsM = useMutation((orderedIds: string[]) => reorderQuestions({ moduleId, orderedIds }), {
    onSuccess: () => {
      invalidate();
      setActionSuccess('Question order updated successfully.');
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError(mapFormsError(error));
    },
  });

  const moveSection = (index: number, direction: 'up' | 'down') => {
    setActionError(null);
    setActionSuccess(null);
    reorderSectionsM.mutate(
      move(
        sections.map((s) => s.id),
        index,
        direction
      )
    );
  };
  // The reorder endpoint assigns DisplayOrder=index+1 across the WHOLE module, so we must always
  // send the full module-wide ordered id list (sections' questions in order, then unassigned),
  // swapping only the affected group — otherwise other sections' questions collide.
  const moveQuestion = (sectionId: string, index: number, direction: 'up' | 'down') => {
    setActionError(null);
    setActionSuccess(null);
    const reordered = move(
      questionsForSection(sectionId).map((q) => q.id),
      index,
      direction
    );
    const globalOrder = [...sections.flatMap((s) => (s.id === sectionId ? reordered : questionsForSection(s.id).map((q) => q.id))), ...unassignedQuestions.map((q) => q.id)];
    reorderQuestionsM.mutate(globalOrder);
  };
  const moveQuestionInGroup = (index: number, direction: 'up' | 'down') => {
    setActionError(null);
    setActionSuccess(null);
    const reordered = move(
      unassignedQuestions.map((q) => q.id),
      index,
      direction
    );
    const globalOrder = [...sections.flatMap((s) => questionsForSection(s.id).map((q) => q.id)), ...reordered];
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
          setActionError(null);
          setActionSuccess(null);
          setSectionForm({ mode: 'edit', section });
        },
      },
      {
        label: 'Delete',
        visuallyHiddenText: `section ${section.title}`,
        onClick: () => {
          setActionError(null);
          setActionSuccess(null);
          setConfirmSection({ id: section.id, title: section.title });
        },
      },
    ];
  };

  return (
    <GeneralLayout breadcrumbs={formsBuilderBreadcrumbs(module ? [{ label: module.name, link: `/admin/forms/${moduleId}` }] : [])} backLink={{ href: '/admin/forms' }}>
      <div style={{ paddingTop: '20px' }}>
        {moduleQuery.isLoading ? (
          <PageLoading />
        ) : !module ? (
          <Paragraph>This form could not be found.</Paragraph>
        ) : (
          <>
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

            {actionSuccess && (
              <GovUKNotificationBanner type="success" title="Form updated" testId="admin-forms-sections-success">
                <Paragraph>{actionSuccess}</Paragraph>
              </GovUKNotificationBanner>
            )}

            {actionError && (
              <div className="govuk-error-summary" role="alert" tabIndex={-1} data-testid="admin-forms-sections-error">
                <h2 className="govuk-error-summary__title">There is a problem</h2>
                <div className="govuk-error-summary__body">
                  <p className="govuk-body">{actionError}</p>
                </div>
              </div>
            )}

            {!isDraft && (
              <div className="govuk-inset-text" data-testid="admin-forms-sections-locked">
                This form has been published and can no longer be changed.
              </div>
            )}

            {isDraft && editingModule && (
              <ModuleForm
                initialName={module.name}
                initialDescription={module.description ?? ''}
                isLoading={updateModuleM.isLoading}
                onSubmit={(input) => {
                  setActionError(null);
                  setActionSuccess(null);
                  updateModuleM.mutate(input);
                }}
                onCancel={() => setEditingModule(false)}
              />
            )}

            {isDraft && !editingModule && !sectionForm && (
              <div className="govuk-button-group">
                <GovUKButton
                  onClick={() => {
                    setActionError(null);
                    setActionSuccess(null);
                    publishModuleM.mutate();
                  }}
                  disabled={publishModuleM.isLoading}
                  data-testid="admin-forms-sections-publish">
                  Publish form
                </GovUKButton>
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={() => {
                    setActionError(null);
                    setActionSuccess(null);
                    setEditingModule(true);
                  }}
                  data-testid="admin-forms-sections-edit-details">
                  Edit form details
                </GovUKButton>
              </div>
            )}

            <H3>Sections</H3>

            {isDraft && sectionForm?.mode === 'add' && (
              <SectionForm
                heading="Add a section"
                submitLabel="Add section"
                isLoading={createSectionM.isLoading}
                onSubmit={(input) => {
                  setActionError(null);
                  setActionSuccess(null);
                  createSectionM.mutate({ title: input.title, description: input.description, helpText: null });
                }}
                onCancel={() => setSectionForm(null)}
              />
            )}

            {isDraft && !sectionForm && !editingModule && (
              <div className="govuk-button-group">
                <GovUKButton
                  onClick={() => {
                    setActionError(null);
                    setActionSuccess(null);
                    setSectionForm({ mode: 'add' });
                  }}
                  data-testid="admin-forms-sections-add-section">
                  Add section
                </GovUKButton>
                <RouterLink to={`/admin/forms/${moduleId}/questions/create`} className="govuk-link" data-testid="admin-forms-sections-add-question">
                  Add a question
                </RouterLink>
              </div>
            )}

            {sections.length === 0 && !sectionForm && <Paragraph>No sections yet. Add a section to start building the form.</Paragraph>}

            {sections.map((section, sectionIndex) => {
              const sectionSlug = slugifyTestId(section.title) || section.id;
              const sectionTestId = `admin-forms-sections-section-${sectionSlug}`;
              const questions = questionsForSection(section.id);

              if (isDraft && sectionForm?.mode === 'edit' && sectionForm.section.id === section.id) {
                return (
                  <SectionForm
                    key={section.id}
                    heading="Edit section"
                    submitLabel="Save section"
                    initialTitle={section.title}
                    initialDescription={section.description ?? ''}
                    isLoading={updateSectionM.isLoading}
                    onSubmit={(input) => {
                      setActionError(null);
                      setActionSuccess(null);
                      updateSectionM.mutate({ id: section.id, input: { title: input.title, description: input.description, helpText: section.helpText } });
                    }}
                    onCancel={() => setSectionForm(null)}
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
                        testId={`${sectionTestId}-section`}
                      />
                    </div>
                  )}

                  {questions.length === 0 ? (
                    <Paragraph>No questions in this section yet.</Paragraph>
                  ) : (
                    <table className="govuk-table forms-sections-questions-table" data-testid={`${sectionTestId}-questions`}>
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
                          const questionTestId = `${sectionTestId}-question-${slugifyTestId(question.code) || question.id}`;
                          const questionActions: ActionMenuItem[] = [
                            ...(questionIndex > 0
                              ? [
                                  {
                                    label: 'Move up',
                                    onClick: () => moveQuestion(section.id, questionIndex, 'up'),
                                    testId: `${questionTestId}-move-up`,
                                  },
                                ]
                              : []),
                            ...(questionIndex < questions.length - 1
                              ? [
                                  {
                                    label: 'Move down',
                                    onClick: () => moveQuestion(section.id, questionIndex, 'down'),
                                    testId: `${questionTestId}-move-down`,
                                  },
                                ]
                              : []),
                            {
                              label: 'Edit',
                              href: `/admin/forms/${moduleId}/questions/${question.id}/edit`,
                              testId: `${questionTestId}-edit`,
                            },
                            {
                              label: 'Delete',
                              onClick: () => {
                                setActionError(null);
                                setActionSuccess(null);
                                setConfirmQuestion({ id: question.id, label: question.label });
                              },
                              testId: `${questionTestId}-delete`,
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
                                  <GovUKActionMenu items={questionActions} buttonLabel="Actions" testId={`${questionTestId}-actions`} />
                                ) : (
                                  <span className="govuk-visually-hidden">No actions available</span>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  )}

                  {isDraft && (
                    <RouterLink to={`/admin/forms/${moduleId}/questions/create?sectionId=${section.id}`} className="govuk-link" data-testid={`${sectionTestId}-add-question`}>
                      Add a question to this section
                    </RouterLink>
                  )}
                </GovUKSummaryCard>
              );
            })}

            {unassignedQuestions.length > 0 && (
              <GovUKSummaryCard testId="admin-forms-sections-unassigned" title="Questions not in a section">
                <Paragraph>These questions are not assigned to a section. Edit each one to move it into a section.</Paragraph>
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
                      const questionTestId = `admin-forms-sections-unassigned-question-${slugifyTestId(question.code) || question.id}`;
                      const questionActions: ActionMenuItem[] = [
                        ...(questionIndex > 0
                          ? [
                              {
                                label: 'Move up',
                                onClick: () => moveQuestionInGroup(questionIndex, 'up'),
                                testId: `${questionTestId}-move-up`,
                              },
                            ]
                          : []),
                        ...(questionIndex < unassignedQuestions.length - 1
                          ? [
                              {
                                label: 'Move down',
                                onClick: () => moveQuestionInGroup(questionIndex, 'down'),
                                testId: `${questionTestId}-move-down`,
                              },
                            ]
                          : []),
                        {
                          label: 'Edit',
                          href: `/admin/forms/${moduleId}/questions/${question.id}/edit`,
                          testId: `${questionTestId}-edit`,
                        },
                        {
                          label: 'Delete',
                          onClick: () => {
                            setActionError(null);
                            setActionSuccess(null);
                            setConfirmQuestion({ id: question.id, label: question.label });
                          },
                          testId: `${questionTestId}-delete`,
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
                              <GovUKActionMenu items={questionActions} buttonLabel="Actions" testId={`${questionTestId}-actions`} />
                            ) : (
                              <span className="govuk-visually-hidden">No actions available</span>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
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
            isLoading={deleteSectionM.isLoading}
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
            isLoading={deleteQuestionM.isLoading}
          />
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormsSections;
