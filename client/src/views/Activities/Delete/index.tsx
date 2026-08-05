import React from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { useMutation, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import { LeadParagraph } from '../../../components/GovUKComponents';
import ConfirmationPanel from '../../../components/Global/Activities/ConfirmationPanel';
import usePageTitle from '../../../hooks/usePageTitle';
import { activitiesListKey, activitiesDeletedKey, activityKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { invalidateReportCaches, useActivityReportContext } from '../reportContext';
import { deleteActivity } from '../mutations';

const ActivitiesDelete = (): React.ReactElement => {
  usePageTitle('Delete activity');
  const { activityId = '' } = useParams<{ activityId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  // Set when the activity was opened from Section 1 of a quarterly return (BLUM-6967).
  const report = useActivityReportContext();

  const name = (location.state as { name?: string } | null)?.name ?? 'this activity';

  const deleteMutation = useMutation(() => deleteActivity(activityId), {
    onSuccess: () => {
      queryClient.invalidateQueries(activitiesListKey);
      queryClient.invalidateQueries(activitiesDeletedKey);
      queryClient.invalidateQueries(activityKey(activityId));
      invalidateReportCaches(queryClient, report.collectionId);
      navigate(report.listPath, { state: { banner: 'deleted', name } });
    },
  });

  const activityPath = report.activityLink(ROUTES.activities.detail(activityId));

  return (
    // The design (Figma 420-29912) shows only a Back link in the chrome — no breadcrumbs. It also omits the beta
    // phase banner, but that is now global chrome on every page per BLUM-7060 AC1 (see BLUM-7102).
    <GeneralLayout backLink={{ href: activityPath, testId: 'activities-delete-back' }}>
      <ConfirmationPanel
        testId="activities-delete"
        title="Are you sure you want to delete this activity?"
        body={
          <>
            <LeadParagraph>Deleting ‘{name}’ will take it off your main list.</LeadParagraph>
            <LeadParagraph>You can restore this activity if you need it.</LeadParagraph>
          </>
        }
        confirmLabel="Delete activity"
        onConfirm={() => deleteMutation.mutate()}
        cancelLabel="Go back to activity"
        onCancel={() => navigate(activityPath)}
        isLoading={deleteMutation.isLoading}
      />
    </GeneralLayout>
  );
};

export default ActivitiesDelete;
