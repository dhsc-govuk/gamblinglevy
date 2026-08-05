import React, { useEffect, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import {
  DeleteConfirmDialog,
  GovUKActionMenu,
  GovUKButton,
  GovUKSummaryCard,
  GovUKTag,
  H1,
  Paragraph,
  type ActionMenuItem,
  type SummaryCardAction,
} from '../../../../components/GovUKComponents';
import SectionForm from '../../../../components/Global/FormDesigner/SectionForm';
import ReorderControls from '../../../../components/Global/FormDesigner/ReorderControls';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { activityFormSchemaKey, adminActivityFormSchemaKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import { slugifyTestId } from '../../../../helpers/testIds';
import type { FormQuestionDto, FormSectionDto } from '../../../../types/formSchema';
import { canManageFormDesigner, formDesignerBreadcrumbs } from '../navigation';
import { getFormSchema, getFormCategories } from '../queries';
import {
  createSection,
  deleteQuestion,
  deleteSection,
  reorderQuestions,
  reorderSections,
  updateSection,
  type SectionInput,
} from '../mutations';
import { mapFormDesignerError } from '../messages';

type SectionFormState = { mode: 'add' } | { mode: 'edit'; section: FormSectionDto } | null;

const move = (ids: string[], index: number, direction: 'up' | 'down'): string[] => {
  const target = direction === 'up' ? index - 1 : index + 1;
  if (target < 0 || target >= ids.length) {
    return ids;
  }
  const next = [...ids];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
};

const FormQuestionsList = (): React.ReactElement => {
  usePageTitle('Activity record questions');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();

  const [sectionForm, setSectionForm] = useState<SectionFormState>(null);
  const [confirmSection, setConfirmSection] = useState<{ id: string; title: string } | null>(null);
  const [confirmQuestion, setConfirmQuestion] = useState<{ id: string; label: string } | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  useEffect(() => {
    if (userRole && !canManageFormDesigner(userRole)) {
      navigate('/admin/home');
    }
  }, [userRole, navigate]);

  const schemaQuery = useQuery(adminActivityFormSchemaKey, getFormSchema, { staleTime: defaultStaleTime });
  const categoriesQuery = useQuery('admin-activity-form-categories', getFormCategories, { staleTime: defaultStaleTime });
  const categories = categoriesQuery.data?.data ?? [];
  const categoryName = (id: string | null): string => (id ? categories.find((category) => category.id === id)?.name ?? 'Unknown category' : 'All categories');
  const sections = (schemaQuery.data?.data.sections ?? []).slice().sort((a, b) => a.displayOrder - b.displayOrder);
  const allQuestions = schemaQuery.data?.data.questions ?? [];
  const questionsForSection = (sectionId: string): FormQuestionDto[] =>
    allQuestions.filter((question) => question.sectionId === sectionId).slice().sort((a, b) => a.displayOrder - b.displayOrder);

  const invalidate = () => {
    queryClient.invalidateQueries(adminActivityFormSchemaKey);
    queryClient.invalidateQueries(activityFormSchemaKey);
  };

  const createSectionM = useMutation((input: SectionInput) => createSection(input), {
    onSuccess: () => { invalidate(); setSectionForm(null); },
    onError: (error) => setActionError(mapFormDesignerError(error)),
  });
  const updateSectionM = useMutation((vars: { id: string; input: SectionInput }) => updateSection(vars.id, vars.input), {
    onSuccess: () => { invalidate(); setSectionForm(null); },
    onError: (error) => setActionError(mapFormDesignerError(error)),
  });
  const deleteSectionM = useMutation((id: string) => deleteSection(id), {
    onSuccess: () => { invalidate(); setConfirmSection(null); },
    onError: (error) => { setActionError(mapFormDesignerError(error)); setConfirmSection(null); },
  });
  const deleteQuestionM = useMutation((id: string) => deleteQuestion(id), {
    onSuccess: () => { invalidate(); setConfirmQuestion(null); },
    onError: (error) => { setActionError(mapFormDesignerError(error)); setConfirmQuestion(null); },
  });
  const reorderSectionsM = useMutation((orderedIds: string[]) => reorderSections(orderedIds), { onSuccess: invalidate });
  const reorderQuestionsM = useMutation(
    (vars: { sectionId: string; orderedIds: string[] }) => reorderQuestions(vars.sectionId, vars.orderedIds),
    { onSuccess: invalidate },
  );

  const moveSection = (index: number, direction: 'up' | 'down') => {
    setActionError(null);
    reorderSectionsM.mutate(move(sections.map((s) => s.id), index, direction));
  };
  const moveQuestion = (sectionId: string, index: number, direction: 'up' | 'down') => {
    setActionError(null);
    const ordered = move(questionsForSection(sectionId).map((q) => q.id), index, direction);
    reorderQuestionsM.mutate({ sectionId, orderedIds: ordered });
  };

  const sectionActions = (section: FormSectionDto): SummaryCardAction[] => [
    { label: 'Edit', visuallyHiddenText: `section ${section.title}`, onClick: () => { setActionError(null); setSectionForm({ mode: 'edit', section }); } },
    { label: 'Delete', visuallyHiddenText: `section ${section.title}`, onClick: () => { setActionError(null); setConfirmSection({ id: section.id, title: section.title }); } },
  ];

  return (
    <GeneralLayout breadcrumbs={formDesignerBreadcrumbs()} backLink={{ href: '/admin/home' }}>
      <div style={{ paddingTop: '20px' }}>
        <H1>Activity record questions</H1>
        <Paragraph>
          Manage the sections and questions that Local Authority and VCSE users answer when they create an activity record.
        </Paragraph>

        {actionError && (
          <div className="govuk-error-summary" role="alert" tabIndex={-1} data-testid="admin-activity-questions-list-error">
            <h2 className="govuk-error-summary__title">There is a problem</h2>
            <div className="govuk-error-summary__body">
              <p className="govuk-body">{actionError}</p>
            </div>
          </div>
        )}

        {schemaQuery.isLoading ? (
          <PageLoading />
        ) : (
          <>
            {sectionForm?.mode === 'add' && (
              <SectionForm
                heading="Add a section"
                submitLabel="Add section"
                categories={categories}
                isLoading={createSectionM.isLoading}
                onSubmit={(input) => { setActionError(null); createSectionM.mutate(input); }}
                onCancel={() => setSectionForm(null)}
              />
            )}

            {!sectionForm && (
              <div className="govuk-button-group">
                <GovUKButton onClick={() => { setActionError(null); setSectionForm({ mode: 'add' }); }} data-testid="admin-activity-questions-list-add-section">
                  Add section
                </GovUKButton>
              </div>
            )}

            {sections.length === 0 && !sectionForm && (
              <Paragraph>No sections yet. Add a section to start building the form.</Paragraph>
            )}

            {sections.map((section, sectionIndex) => {
              const sectionSlug = slugifyTestId(section.title) || section.id;
              const sectionTestId = `admin-activity-questions-list-section-${sectionSlug}`;
              const questions = questionsForSection(section.id);

              if (sectionForm?.mode === 'edit' && sectionForm.section.id === section.id) {
                return (
                  <SectionForm
                    key={section.id}
                    heading="Edit section"
                    submitLabel="Save section"
                    initialTitle={section.title}
                    initialDescription={section.description ?? ''}
                    initialCategoryId={section.categoryId}
                    categories={categories}
                    isLoading={updateSectionM.isLoading}
                    onSubmit={(input) => { setActionError(null); updateSectionM.mutate({ id: section.id, input }); }}
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
                      <span className="govuk-!-margin-left-2"><GovUKTag colour="turquoise" className="govuk-tag--nowrap">{categoryName(section.categoryId)}</GovUKTag></span>
                      {!section.isActive && <span className="govuk-!-margin-left-2"><GovUKTag colour="grey">Hidden</GovUKTag></span>}
                    </>
                  }
                  actions={sectionActions(section)}
                >
                  {section.description && <Paragraph>{section.description}</Paragraph>}

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

                  {questions.length === 0 ? (
                    <Paragraph>No questions in this section yet.</Paragraph>
                  ) : (
                    <table className="govuk-table" data-testid={`${sectionTestId}-questions`}>
                      <thead className="govuk-table__head">
                        <tr className="govuk-table__row">
                          <th scope="col" className="govuk-table__header">Question</th>
                          <th scope="col" className="govuk-table__header">Type</th>
                          <th scope="col" className="govuk-table__header">Code</th>
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
                              ? [{
                                  label: 'Move up',
                                  onClick: () => moveQuestion(section.id, questionIndex, 'up'),
                                  testId: `${questionTestId}-move-up`,
                                }]
                              : []),
                            ...(questionIndex < questions.length - 1
                              ? [{
                                  label: 'Move down',
                                  onClick: () => moveQuestion(section.id, questionIndex, 'down'),
                                  testId: `${questionTestId}-move-down`,
                                }]
                              : []),
                            {
                              label: 'Edit',
                              href: `/admin/activity-record-questions/${question.id}/edit`,
                              testId: `${questionTestId}-edit`,
                            },
                            {
                              label: 'Delete',
                              onClick: () => { setActionError(null); setConfirmQuestion({ id: question.id, label: question.label }); },
                              testId: `${questionTestId}-delete`,
                            },
                          ];
                          return (
                            <tr className="govuk-table__row" key={question.id} data-testid={questionTestId}>
                              <td className="govuk-table__cell">
                                {question.label}
                                {question.isRequired && <span className="govuk-!-margin-left-1"><GovUKTag colour="purple">Required</GovUKTag></span>}
                                {!question.isActive && <span className="govuk-!-margin-left-1"><GovUKTag colour="grey">Hidden</GovUKTag></span>}
                              </td>
                              <td className="govuk-table__cell">
                                <GovUKTag colour="blue">{question.questionType}</GovUKTag>
                              </td>
                              <td className="govuk-table__cell">
                                <span className="govuk-hint govuk-!-margin-bottom-0">{question.code}</span>
                              </td>
                              <td className="govuk-table__cell govuk-table__cell--numeric">
                                <GovUKActionMenu
                                  items={questionActions}
                                  buttonLabel="Actions"
                                  testId={`${questionTestId}-actions`}
                                />
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  )}

                  <RouterLink
                    to={`/admin/activity-record-questions/create?sectionId=${section.id}`}
                    className="govuk-link"
                    data-testid={`${sectionTestId}-add-question`}
                  >
                    Add a question to this section
                  </RouterLink>
                </GovUKSummaryCard>
              );
            })}
          </>
        )}

        {confirmSection && (
          <DeleteConfirmDialog
            testId="admin-activity-questions-section-delete"
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
            testId="admin-activity-questions-delete"
            open
            title="Delete this question?"
            message="Saved answers are kept, but the question will no longer appear on the form. To hide it reversibly, edit it and clear ‘Show this question on the form’ instead."
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

export default FormQuestionsList;
