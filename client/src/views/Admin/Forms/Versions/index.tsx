import React, { useEffect } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { GovUKInsetText, GovUKTable, H1, NotFound, PageLoading, Paragraph, type Column } from '../../../../components/GovUKComponents';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle, { pageTitleWithName } from '../../../../hooks/usePageTitle';
import { defaultStaleTime, formModuleKey, formModuleVersionsKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { composeTestId } from '../../../../helpers/testIds';
import { formatDateTime } from '../../../../helpers/dateDisplay';
import { isNotFoundError } from '../../../../helpers/httpErrors';
import { shouldRetryQuery } from '../../../../helpers/queryRetry';
import { canManageForms, formsBuilderBreadcrumbs } from '../navigation';
import { getFormModule, getFormModuleVersions, type FormModuleVersionSummaryDto } from '../queries';

const TABLE_ID = 'admin-forms-versions-table';

// A 404 is a verdict (unknown form, or the flag is off), so it is not retried; everything else keeps the shared rule.
const retryUnlessNotFound = (failureCount: number, error: unknown): boolean => !isNotFoundError(error) && shouldRetryQuery(failureCount, error);

const versionColumns = (moduleId: string): Column<FormModuleVersionSummaryDto>[] => [
  {
    key: 'versionNumber',
    header: 'Version',
    render: (version) => (
      <RouterLink to={ROUTES.admin.forms.version(moduleId, version.versionNumber)} className="govuk-link" data-testid={composeTestId(TABLE_ID, 'row', version.id, 'view')}>
        Version {version.versionNumber}
      </RouterLink>
    ),
  },
  { key: 'publishedAt', header: 'Published', render: (version) => formatDateTime(version.publishedAt) },
  { key: 'publishedByName', header: 'Published by', render: (version) => version.publishedByName ?? 'Unknown user' },
];

// BLUM-9102: every saved version of a form, newest first. Each publish that changed the form saved one; a form
// published before versioning was switched on has none until it is published again.
const FormVersions = (): React.ReactElement => {
  const { moduleId = '' } = useParams<{ moduleId: string }>();
  const navigate = useNavigate();
  const { userRole } = useAuthProvider();

  useEffect(() => {
    if (userRole && !canManageForms(userRole)) {
      navigate(ROUTES.admin.home);
    }
  }, [userRole, navigate]);

  const moduleQuery = useQuery({
    queryKey: formModuleKey(moduleId),
    queryFn: () => getFormModule(moduleId),
    staleTime: defaultStaleTime,
    enabled: Boolean(moduleId),
    retry: retryUnlessNotFound,
  });
  const versionsQuery = useQuery({
    queryKey: formModuleVersionsKey(moduleId),
    queryFn: () => getFormModuleVersions(moduleId),
    enabled: Boolean(moduleId),
    retry: retryUnlessNotFound,
  });
  const module = moduleQuery.data?.data;
  const versions = versionsQuery.data?.data ?? [];

  usePageTitle(pageTitleWithName('Previous versions', module?.name));

  // The endpoint answers a bodiless 404 only when BLUM_9087 has been switched off since this tab loaded.
  if (isNotFoundError(versionsQuery.error)) {
    return <NotFound goBack={navigate} />;
  }

  return (
    <GeneralLayout
      breadcrumbs={formsBuilderBreadcrumbs([{ label: module?.name ?? 'Form', link: ROUTES.admin.forms.module(moduleId) }])}
      backLink={{ href: ROUTES.admin.forms.module(moduleId) }}>
      <div className="govuk-!-padding-top-4" data-testid="admin-forms-versions">
        {moduleQuery.isLoading || versionsQuery.isLoading ? (
          <PageLoading />
        ) : !module ? (
          <>
            <H1>{isNotFoundError(moduleQuery.error) ? 'Form not found' : 'Sorry, there is a problem'}</H1>
            <Paragraph data-testid="admin-forms-versions-not-found">
              {isNotFoundError(moduleQuery.error) ? 'This form could not be found.' : 'This form could not be loaded. Reload the page to try again.'}
            </Paragraph>
            <RouterLink to={ROUTES.admin.forms.list} className="govuk-link" data-testid="admin-forms-versions-back-to-forms">
              Return to forms
            </RouterLink>
          </>
        ) : (
          <>
            <span className="govuk-caption-l">{module.name}</span>
            <H1>Previous versions</H1>
            <Paragraph>A version is saved each time this form is published with changes. Open a version to see its questions or restore it.</Paragraph>

            {versionsQuery.isError ? (
              <Paragraph data-testid="admin-forms-versions-error">The versions of this form could not be loaded. Reload the page to try again.</Paragraph>
            ) : versions.length === 0 ? (
              <GovUKInsetText data-testid="admin-forms-versions-empty">This form has no saved versions yet. A version is saved the next time it is published.</GovUKInsetText>
            ) : (
              <GovUKTable<FormModuleVersionSummaryDto>
                label="Previous versions"
                testId={TABLE_ID}
                data={versions}
                columns={versionColumns(moduleId)}
                keyExtractor={(version) => version.id}
              />
            )}
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormVersions;
