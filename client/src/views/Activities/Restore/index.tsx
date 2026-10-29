import React from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import ConfirmationPanel from '../../../components/Global/Activities/ConfirmationPanel';
import usePageTitle from '../../../hooks/usePageTitle';
import { activitiesListKey, activitiesDeletedKey, activityKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { invalidateReportCaches, useActivityReportContext } from '../reportContext';
import { restoreActivity } from '../mutations';

const ActivitiesRestore = (): React.ReactElement => {
  usePageTitle('Restore activity');
  const { activityId = '' } = useParams<{ activityId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  // Set when the deleted list was reached from Section 1 of a quarterly return (BLUM-6967).
  const report = useActivityReportContext();

  const name = (location.state as { name?: string } | null)?.name ?? 'this activity';

  const restoreMutation = useMutation({
    mutationFn: () => restoreActivity(activityId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: activitiesListKey });
      queryClient.invalidateQueries({ queryKey: activitiesDeletedKey });
      queryClient.invalidateQueries({ queryKey: activityKey(activityId) });
      // A restored activity rejoins the main list, so the report's derived state has to be re-read.
      invalidateReportCaches(queryClient, report.collectionId);
      navigate(report.activityLink(ROUTES.activities.deleted), { state: { banner: 'restored', name } });
    },
  });

  const deletedActivitiesPath = report.activityLink(ROUTES.activities.deleted);

  return (
    // The design (Figma 420-30143) shows only a Back link in the chrome — no breadcrumbs. It also omits the beta
    // phase banner, but that is now global chrome on every page per BLUM-7060 AC1 (see BLUM-7102).
    <GeneralLayout backLink={{ href: deletedActivitiesPath, testId: 'activities-restore-back' }}>
      <ConfirmationPanel
        testId="activities-restore"
        title="Are you sure you want to restore this activity?"
        body={<>Restoring ‘{name}’ will move it back to your main list so you can continue to work on it.</>}
        confirmLabel="Restore activity"
        onConfirm={() => restoreMutation.mutate()}
        cancelLabel="Go back to deleted activities"
        onCancel={() => navigate(deletedActivitiesPath)}
        isLoading={restoreMutation.isPending}
      />
    </GeneralLayout>
  );
};

export default ActivitiesRestore;
