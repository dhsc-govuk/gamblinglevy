import React, { useState } from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import usePageTitle from '../../../hooks/usePageTitle';
import { reportContextKey, reportTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import ActivitiesList from '../../Activities/List';
import { confirmActivities } from '../mutations';
import { mapReportError } from '../messages';
import { useReportContext } from '../context';

// The quarterly report's "Manage your list of activities" step (BLUM-6611): the standard activities
// list with a confirm footer. Confirming marks the task-list step completed and clears review flags.
const ConfirmActivities = (): React.ReactElement => {
  usePageTitle('Manage your list of activities');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const [confirmAttempts, setConfirmAttempts] = useState(0);

  const { collectionId, context, isLoading, isError } = useReportContext();

  const confirmMutation = useMutation(confirmActivities, {
    onSuccess: () => {
      queryClient.invalidateQueries(reportContextKey(collectionId));
      queryClient.invalidateQueries(reportTaskListKey(collectionId));
      navigate(ROUTES.reports.tasks(collectionId), { state: { banner: 'activitiesConfirmed' } });
    },
    onError: (error) => {
      setConfirmError(mapReportError(error).message);
      setConfirmAttempts((attempts) => attempts + 1);
    },
  });

  // Unknown collection, another organisation's, or a link that predates the collection-scoped flow.
  if (isError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  if (isLoading) {
    return (
      <GeneralLayout backLink={{ href: ROUTES.reports.tasks(collectionId), testId: 'reports-confirm-activities-back' }}>
        <div style={{ paddingTop: '20px' }}>
          <PageLoading />
        </div>
      </GeneralLayout>
    );
  }

  // The report has not been created yet — back to the intro page for this collection.
  if (!context || !context.submissionId) {
    return <Navigate to={ROUTES.reports.quarterly(collectionId)} replace />;
  }

  return (
    <ActivitiesList
      mode="report"
      reportCollectionId={collectionId}
      backLink={{ href: ROUTES.reports.tasks(collectionId), testId: 'reports-confirm-activities-back' }}
      onConfirm={() => confirmMutation.mutate(collectionId)}
      confirmLoading={confirmMutation.isLoading}
      confirmError={confirmError}
      confirmAttempts={confirmAttempts}
    />
  );
};

export default ConfirmActivities;
