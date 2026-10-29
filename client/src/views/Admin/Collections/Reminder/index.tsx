import React, { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, LoadingBox } from '../../../../components/GovUKComponents';
import ConfirmationPanel from '../../../../components/Global/Activities/ConfirmationPanel';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionsListKey, collectionKey } from '../../../../helpers/queriesParams';
import { getCollection } from '../queries';
import { sendManualReminder } from '../mutations';
import { mapCollectionError } from '../messages';
import './styles.css';

const CollectionsReminder = (): React.ReactElement => {
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [reminderError, setReminderError] = useState<string | null>(null);
  const [attempts, setAttempts] = useState(0);
  usePageTitle(`${reminderError ? 'Error: ' : ''}Send a reminder`);

  const { isLoading } = useQuery({ queryKey: collectionKey(collectionId), queryFn: () => getCollection(collectionId), enabled: !!collectionId });
  const reminderMutation = useMutation({
    mutationFn: () => sendManualReminder(collectionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: collectionsListKey });
      queryClient.invalidateQueries({ queryKey: collectionKey(collectionId) });
      navigate(`/admin/collections/${collectionId}`, {
        state: {
          banner: {
            title: 'Reminder sent',
            body: 'We sent an email to all reporting organisations that have not yet submitted their information.',
          },
        },
      });
    },
    onError: (error) => {
      setReminderError(mapCollectionError(error));
      setAttempts((current) => current + 1);
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
        <ErrorSummary
          heading="There is a problem"
          errors={reminderError ? [{ targetName: 'admin-collections-reminder-confirm', text: reminderError }] : []}
          focusTrigger={attempts}
          testId="admin-collections-reminder-errors"
        />
        <ConfirmationPanel
          testId="admin-collections-reminder"
          title="Are you sure you want to send a reminder?"
          body={
            <p className="govuk-body" style={{ color: '#ffffff' }}>
              This will immediately send an email to all reporting organisations that have not yet submitted their data. If you want to schedule a reminder for a different date, go
              back to the data collection and edit your automatic reminders.
            </p>
          }
          confirmLabel="Send reminder now"
          onConfirm={() => {
            setReminderError(null);
            reminderMutation.mutate();
          }}
          cancelLabel="Go back to data collection"
          onCancel={() => navigate(`/admin/collections/${collectionId}`)}
          isLoading={reminderMutation.isPending}
        />
      </div>
    </GeneralLayout>
  );
};

export default CollectionsReminder;
