import React from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { LoadingBox } from '../../../../components/GovUKComponents';
import ConfirmationPanel from '../../../../components/Global/Activities/ConfirmationPanel';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionsListKey, collectionsDeletedKey, collectionKey } from '../../../../helpers/queriesParams';
import { getCollection } from '../queries';
import { restoreCollection } from '../mutations';
import './styles.css';

const CollectionsRestore = (): React.ReactElement => {
  usePageTitle('Restore data collection');
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery(collectionKey(collectionId), () => getCollection(collectionId), {
    enabled: !!collectionId,
  });
  const name = data?.data?.name ?? 'this data collection';

  const restoreMutation = useMutation(() => restoreCollection(collectionId), {
    onSuccess: () => {
      queryClient.invalidateQueries(collectionsListKey);
      queryClient.invalidateQueries(collectionsDeletedKey);
      queryClient.invalidateQueries(collectionKey(collectionId));
      navigate('/admin/collections/deleted', {
        state: {
          banner: {
            title: 'Data collection restored',
            body: `To view the data collection ‘${name}’, `,
            link: {
              label: 'return to your data collections.',
              href: '/admin/collections',
            },
          },
        },
      });
    },
  });

  if (isLoading) {
    return (
      <GeneralLayout backLink={{ href: '/admin/collections/deleted' }}>
        <LoadingBox loading>
          <div style={{ paddingTop: '20px', minHeight: '200px' }} />
        </LoadingBox>
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout backLink={{ href: '/admin/collections/deleted', onClick: () => navigate('/admin/collections/deleted') }}>
      <div className="collections-restore-page">
        <ConfirmationPanel
          testId="admin-collections-restore"
          title="Are you sure you want to restore this data collection?"
          body={
            <p className="govuk-body" style={{ color: '#ffffff' }}>
              Restoring the data collection ‘{name}’ will move it to your list of data collections. You will be able to edit it.
            </p>
          }
          confirmLabel="Restore data collection"
          onConfirm={() => restoreMutation.mutate()}
          cancelLabel="Go back to deleted data collections"
          onCancel={() => navigate('/admin/collections/deleted')}
          isLoading={restoreMutation.isLoading}
        />
      </div>
    </GeneralLayout>
  );
};

export default CollectionsRestore;
