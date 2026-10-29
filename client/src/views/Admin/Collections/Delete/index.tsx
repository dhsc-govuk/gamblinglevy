import React from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { LoadingBox } from '../../../../components/GovUKComponents';
import ConfirmationPanel from '../../../../components/Global/Activities/ConfirmationPanel';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionsListKey, collectionsDeletedKey, collectionKey } from '../../../../helpers/queriesParams';
import { getCollection } from '../queries';
import { deleteCollection } from '../mutations';
import './styles.css';

const CollectionsDelete = (): React.ReactElement => {
  usePageTitle('Delete data collection');
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({ queryKey: collectionKey(collectionId), queryFn: () => getCollection(collectionId), enabled: !!collectionId });
  const name = data?.data?.name ?? 'this data collection';

  const deleteMutation = useMutation({
    mutationFn: () => deleteCollection(collectionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: collectionsListKey });
      queryClient.invalidateQueries({ queryKey: collectionsDeletedKey });
      queryClient.invalidateQueries({ queryKey: collectionKey(collectionId) });
      navigate('/admin/collections', {
        state: {
          banner: {
            title: 'Data collection deleted',
            body: `‘${name}’ has been moved to your deleted data collections.`,
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
      <div className="collections-delete-page">
        <ConfirmationPanel
          testId="admin-collections-delete"
          title="Are you sure you want to delete this data collection?"
          body={
            <p className="govuk-body" style={{ color: '#ffffff' }}>
              Deleting the data collection ‘{name}’ will move it to your deleted data collections list. You will still be able to view it.
            </p>
          }
          confirmLabel="Delete data collection"
          onConfirm={() => deleteMutation.mutate()}
          cancelLabel="Go back to data collection"
          onCancel={() => navigate(`/admin/collections/${collectionId}`)}
          isLoading={deleteMutation.isPending}
        />
      </div>
    </GeneralLayout>
  );
};

export default CollectionsDelete;
