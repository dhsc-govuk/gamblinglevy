import React from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { LoadingBox } from '../../../../components/GovUKComponents';
import ConfirmationPanel from '../../../../components/Global/Activities/ConfirmationPanel';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionsListKey, collectionKey } from '../../../../helpers/queriesParams';
import { getCollection } from '../queries';
import { closeCollection } from '../mutations';
import './styles.css';

const CollectionsClose = (): React.ReactElement => {
  usePageTitle('Close data collection');
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({ queryKey: collectionKey(collectionId), queryFn: () => getCollection(collectionId), enabled: !!collectionId });
  const name = data?.data?.name ?? 'this data collection';

  const closeMutation = useMutation({
    mutationFn: () => closeCollection(collectionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: collectionsListKey });
      queryClient.invalidateQueries({ queryKey: collectionKey(collectionId) });
      navigate(`/admin/collections/${collectionId}`, {
        state: {
          banner: {
            title: 'Data collection closed',
            body: 'Organisations can no longer submit information for this data collection.',
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
      <div className="collections-close-page">
        <ConfirmationPanel
          testId="admin-collections-close"
          title="Are you sure you want to close this data collection?"
          body={
            <>
              <p className="govuk-body" style={{ color: '#ffffff' }}>
                If you close the data collection ‘{name}’, organisations will no longer be able to submit new data. They will still be able to see the data collection details.
              </p>
              <p className="govuk-body" style={{ color: '#ffffff' }}>
                Any data already submitted by organisations will be saved.
              </p>
              <p className="govuk-body" style={{ color: '#ffffff' }}>
                If you close the data collection, this cannot be undone.
              </p>
            </>
          }
          confirmLabel="Close data collection"
          onConfirm={() => closeMutation.mutate()}
          cancelLabel="Go back to data collection"
          onCancel={() => navigate(`/admin/collections/${collectionId}`)}
          isLoading={closeMutation.isPending}
        />
      </div>
    </GeneralLayout>
  );
};

export default CollectionsClose;
