import React, { useEffect, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { GovUKButton, GovUKNotificationBanner, GovUKSummaryCard, GovUKTag, H1, Paragraph, type TagColour } from '../../../../components/GovUKComponents';
import ModuleForm from '../../../../components/Global/Forms/ModuleForm';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { defaultStaleTime, formModulesKey } from '../../../../helpers/queriesParams';
import { slugifyTestId } from '../../../../helpers/testIds';
import { canManageForms, formsBuilderBreadcrumbs } from '../navigation';
import { getFormModules, type FormModuleDto, type FormModuleStatus } from '../queries';
import { createFormModule, type CreateFormModuleInput } from '../mutations';
import { mapFormsError } from '../messages';

// Drafts are editable; published forms are locked; archived forms are retired. The tag colour and
// label make the lifecycle state scannable at a glance on the landing page.
const STATUS_TAG: Record<FormModuleStatus, { colour: TagColour; label: string }> = {
  draft: { colour: 'grey', label: 'Draft' },
  published: { colour: 'green', label: 'Published' },
  archived: { colour: 'red', label: 'Archived' },
};

const FormModulesList = (): React.ReactElement => {
  usePageTitle('Forms');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();

  const [showAddForm, setShowAddForm] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);

  useEffect(() => {
    if (userRole && !canManageForms(userRole)) {
      navigate('/admin/home');
    }
  }, [userRole, navigate]);

  const modulesQuery = useQuery(formModulesKey, getFormModules, { staleTime: defaultStaleTime });
  const modules = (modulesQuery.data?.data ?? []).slice().sort((a, b) => a.displayOrder - b.displayOrder);

  const createModuleM = useMutation((input: CreateFormModuleInput) => createFormModule(input), {
    onSuccess: (_response, input) => {
      queryClient.invalidateQueries(formModulesKey);
      setShowAddForm(false);
      setActionSuccess(`${input.name} was created successfully.`);
    },
    onError: (error) => {
      setActionSuccess(null);
      setActionError(mapFormsError(error));
    },
  });

  return (
    <GeneralLayout breadcrumbs={formsBuilderBreadcrumbs()} backLink={{ href: '/admin/home' }}>
      <div style={{ paddingTop: '20px' }}>
        <H1>Forms</H1>
        <Paragraph>
          Build and manage the data-collection forms that Local Authority and VCSE organisations complete. Each form is a reusable template made of sections and questions.
        </Paragraph>

        {actionSuccess && (
          <GovUKNotificationBanner type="success" title="Form created" testId="admin-forms-modules-success">
            <Paragraph>{actionSuccess}</Paragraph>
          </GovUKNotificationBanner>
        )}

        {actionError && (
          <div className="govuk-error-summary" role="alert" tabIndex={-1} data-testid="admin-forms-modules-error">
            <h2 className="govuk-error-summary__title">There is a problem</h2>
            <div className="govuk-error-summary__body">
              <p className="govuk-body">{actionError}</p>
            </div>
          </div>
        )}

        {modulesQuery.isLoading ? (
          <PageLoading />
        ) : (
          <>
            {showAddForm && (
              <ModuleForm
                isSubmitting={createModuleM.isLoading}
                onSubmit={(input) => {
                  setActionError(null);
                  setActionSuccess(null);
                  createModuleM.mutate(input);
                }}
                onCancel={() => setShowAddForm(false)}
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
              const moduleTestId = `admin-forms-modules-module-${moduleSlug}`;
              const statusTag = STATUS_TAG[module.status];

              return (
                <GovUKSummaryCard
                  key={module.id}
                  testId={moduleTestId}
                  title={
                    <>
                      {module.name}
                      <span className="govuk-!-margin-left-2">
                        <GovUKTag colour={statusTag.colour} data-testid={`${moduleTestId}-status`}>
                          {statusTag.label}
                        </GovUKTag>
                      </span>
                    </>
                  }>
                  {module.description && <Paragraph>{module.description}</Paragraph>}
                  <p className="govuk-hint govuk-!-margin-bottom-3">{module.code}</p>
                  <RouterLink to={`/admin/forms/${module.id}`} className="govuk-link" data-testid={`${moduleTestId}-manage`}>
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
