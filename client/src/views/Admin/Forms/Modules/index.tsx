import React, { useEffect, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, GovUKNotificationBanner, GovUKSummaryCard, GovUKTag, H1, Paragraph, type TagColour } from '../../../../components/GovUKComponents';
import ModuleForm from '../../../../components/Global/Forms/ModuleForm';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { defaultStaleTime, formModulesKey } from '../../../../helpers/queriesParams';
import { composeTestId, slugifyTestId } from '../../../../helpers/testIds';
import { ROUTES } from '../../../../helpers/routes';
import { canManageForms, formsBuilderBreadcrumbs } from '../navigation';
import { getFormModules, type FormModuleDto, type FormModuleStatus } from '../queries';
import { createFormModule, type CreateFormModuleInput } from '../mutations';
import { formsErrorCode, mapFormsError } from '../messages';

// Drafts are editable; published forms are locked; archived forms are retired. The tag colour and
// label make the lifecycle state scannable at a glance on the landing page.
const STATUS_TAG: Record<FormModuleStatus, { colour: TagColour; label: string }> = {
  draft: { colour: 'grey', label: 'Draft' },
  published: { colour: 'green', label: 'Published' },
  archived: { colour: 'red', label: 'Archived' },
};

const FormModulesList = (): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();

  const [showAddForm, setShowAddForm] = useState(false);
  // A refused create, linked from the error summary to the field it concerns.
  const [actionError, setActionError] = useState<{ text: string; target: string } | null>(null);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);
  // Bumped on every attempt, so the summary takes focus each time a refusal arrives (BLUM-8760).
  const [actionAttempts, setActionAttempts] = useState(0);
  // 1 only after a create, which closes the form the user was in: the success banner takes focus then.
  const [successFocus, setSuccessFocus] = useState(0);
  usePageTitle(actionError ? 'Error: Forms' : 'Forms');

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

  const modulesQuery = useQuery({ queryKey: formModulesKey, queryFn: getFormModules, staleTime: defaultStaleTime });
  const modules = (modulesQuery.data?.data ?? []).slice().sort((a, b) => a.displayOrder - b.displayOrder);

  const createModuleM = useMutation({
    mutationFn: (input: CreateFormModuleInput) => createFormModule(input),
    onSuccess: (_response, input) => {
      queryClient.invalidateQueries({ queryKey: formModulesKey });
      setShowAddForm(false);
      setActionSuccess(`${input.name} was created successfully.`);
      setSuccessFocus(1);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError({
        text: mapFormsError(error),
        target: formsErrorCode(error) === 'DUPLICATE_MODULE_CODE' ? 'admin-forms-module-form-code' : 'admin-forms-module-form-name',
      });
    },
  });

  return (
    <GeneralLayout breadcrumbs={formsBuilderBreadcrumbs()} backLink={{ href: ROUTES.admin.home }}>
      <div className="govuk-!-padding-top-4">
        <ErrorSummary errors={actionError ? [{ targetName: actionError.target, text: actionError.text }] : []} focusTrigger={actionAttempts} testId="admin-forms-modules-error" />
        <H1>Forms</H1>
        <Paragraph>
          Build and manage the data-collection forms that Local Authority and VCSE organisations complete. Each form is a reusable template made of sections and questions.
        </Paragraph>

        {actionSuccess && (
          <GovUKNotificationBanner type="success" title="Form created" focusTrigger={successFocus} testId="admin-forms-modules-success">
            <Paragraph>{actionSuccess}</Paragraph>
          </GovUKNotificationBanner>
        )}

        {modulesQuery.isLoading ? (
          <PageLoading />
        ) : (
          <>
            {showAddForm && (
              <ModuleForm
                isSubmitting={createModuleM.isPending}
                onSubmit={(input) => {
                  beginAction();
                  createModuleM.mutate(input);
                }}
                onCancel={() => {
                  setActionError(null);
                  setShowAddForm(false);
                }}
              />
            )}

            {!showAddForm && (
              <div className="govuk-button-group">
                <GovUKButton
                  onClick={() => {
                    setActionError(null);
                    setActionSuccess(null);
                    setShowAddForm(true);
                  }}
                  data-testid="admin-forms-modules-add">
                  Add form
                </GovUKButton>
              </div>
            )}

            {modules.length === 0 && !showAddForm && <Paragraph data-testid="admin-forms-modules-empty">No forms yet. Add a form to start building.</Paragraph>}

            {modules.map((module: FormModuleDto) => {
              const moduleSlug = slugifyTestId(module.code) || module.id;
              const moduleTestId = composeTestId('admin-forms-modules-module', moduleSlug);
              const statusTag = STATUS_TAG[module.status];

              return (
                <GovUKSummaryCard
                  key={module.id}
                  testId={moduleTestId}
                  title={
                    <>
                      {module.name}
                      <span className="govuk-!-margin-left-2">
                        <GovUKTag colour={statusTag.colour} data-testid={composeTestId(moduleTestId, 'status')}>
                          {statusTag.label}
                        </GovUKTag>
                      </span>
                    </>
                  }>
                  {module.description && <Paragraph>{module.description}</Paragraph>}
                  <p className="govuk-hint govuk-!-margin-bottom-3">{module.code}</p>
                  <RouterLink to={ROUTES.admin.forms.module(module.id)} className="govuk-link" data-testid={composeTestId(moduleTestId, 'manage')}>
                    Manage questions
                    <span className="govuk-visually-hidden"> for {module.name}</span>
                  </RouterLink>
                </GovUKSummaryCard>
              );
            })}
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormModulesList;
