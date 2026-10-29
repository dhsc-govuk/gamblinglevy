import React from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { LoadingBox } from '../../../../components/GovUKComponents';
import ConfirmationPanel from '../../../../components/Global/Activities/ConfirmationPanel';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionsListKey, collectionKey } from '../../../../helpers/queriesParams';
import { getCollection } from '../queries';
import { revertToDraft } from '../mutations';
import './styles.css';

const CollectionsRevert = (): React.ReactElement => {
  usePageTitle('Revert to draft');
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({ queryKey: collectionKey(collectionId), queryFn: () => getCollection(collectionId), enabled: !!collectionId });
  const name = data?.data?.name ?? 'this data collection';

  const revertMutation = useMutation({
    mutationFn: () => revertToDraft(collectionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: collectionsListKey });
      queryClient.invalidateQueries({ queryKey: collectionKey(collectionId) });
      navigate(`/admin/collections/${collectionId}`, {
        state: {
          banner: {
            title: 'Data collection reverted to draft',
            body: 'You can now edit and republish the data collection.',
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
      <div className="collections-revert-page">
        <ConfirmationPanel
          testId="admin-collections-revert"
          title="Are you sure you want to revert this data collection to a draft?"
          body={
            <>
              <p className="govuk-body" style={{ color: '#ffffff' }}>
                If you revert the data collection ‘{name}’ to draft, organisations will no longer have access. This means they will not be able to view it or submit new data.
              </p>
              <p className="govuk-body" style={{ color: '#ffffff' }}>
                Any data already submitted by organisations will be saved.
              </p>
            </>
          }
          confirmLabel="Revert to draft"
          onConfirm={() => revertMutation.mutate()}
          cancelLabel="Go back to data collection"
          onCancel={() => navigate(`/admin/collections/${collectionId}`)}
          isLoading={revertMutation.isPending}
        />
      </div>
    </GeneralLayout>
  );
};

export default CollectionsRevert;
