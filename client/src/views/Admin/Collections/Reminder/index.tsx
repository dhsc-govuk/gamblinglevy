import React from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { LoadingBox } from '../../../../components/GovUKComponents';
import ConfirmationPanel from '../../../../components/Global/Activities/ConfirmationPanel';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionsListKey, collectionKey } from '../../../../helpers/queriesParams';
import { getCollection } from '../queries';
import { sendManualReminder } from '../mutations';
import './styles.css';

const CollectionsReminder = (): React.ReactElement => {
  usePageTitle('Send a reminder');
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { isLoading } = useQuery(collectionKey(collectionId), () => getCollection(collectionId), {
    enabled: !!collectionId,
  });
  const reminderMutation = useMutation(() => sendManualReminder(collectionId), {
    onSuccess: () => {
      queryClient.invalidateQueries(collectionsListKey);
      queryClient.invalidateQueries(collectionKey(collectionId));
      navigate(`/admin/collections/${collectionId}`, {
        state: {
          banner: {
            title: 'Reminder sent',
            body: 'We sent an email to all reporting organisations that have not yet submitted their information.',
          },
        },
      });
    },
  });

  if (isLoading) {
    return (
      <GeneralLayout backLink={{ href: `/admin/collections/${collectionId}` }}>
        <LoadingBox loading>
          <div style={{ paddingTop: '20px', minHeight: '200px' }} />
        </LoadingBox>
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout backLink={{ href: `/admin/collections/${collectionId}`, onClick: () => navigate(`/admin/collections/${collectionId}`) }}>
      <div className="collections-reminder-page">
        <ConfirmationPanel
          testId="admin-collections-reminder"
          title="Are you sure you want to send a reminder?"
          body={
            <p className="govuk-body" style={{ color: '#ffffff' }}>
              This will immediately send an email to all reporting organisations that have not yet submitted their data. If you want to schedule a reminder for a different date, go back to the data collection and edit your automatic reminders.
            </p>
          }
          confirmLabel="Send reminder now"
          onConfirm={() => reminderMutation.mutate()}
          cancelLabel="Go back to data collection"
          onCancel={() => navigate(`/admin/collections/${collectionId}`)}
          isLoading={reminderMutation.isLoading}
        />
      </div>
    </GeneralLayout>
  );
};

export default CollectionsReminder;
