import React from 'react';
import { useLocation, useParams } from 'react-router-dom';
import { useQuery } from 'react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKInsetText, H1, LoadingBox } from '../../../../components/GovUKComponents';
import CollectionWizard, { type CollectionWizardDiscardState } from '../../../../components/Global/Collections/CollectionWizard';
import CollectionEditor from '../../../../components/Global/Collections/CollectionEditor';
import usePageTitle, { pageTitleWithName } from '../../../../hooks/usePageTitle';
import { collectionKey } from '../../../../helpers/queriesParams';
import { getCollection } from '../queries';
import { hydrateCollectionState } from '../reducer';
import { canEditStatus, isDraftStatus } from '../display';
import './styles.css';

// Loads a data collection and branches the edit experience by status (mirrors Activities/Edit):
//   - draft     → resume the linear "Add data collection" wizard (Save and come back later).
//   - scheduled / open → section-by-section "check your answers" editor.
//   - closed    → read-only notice (no longer editable).
const CollectionsEdit = (): React.ReactElement => {
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const location = useLocation();
  const restoredWizardState = location.state as CollectionWizardDiscardState | null;

  const { data, isLoading } = useQuery(collectionKey(collectionId), () => getCollection(collectionId), {
    enabled: !!collectionId,
  });
  const c = data?.data;

  // CollectionWizard/CollectionEditor set their own step-specific title once rendered — this only
  // supplies a title for the branches that don't render either of those (loading, and the closed
  // read-only notice).
  usePageTitle(!c ? 'Change data collection' : isDraftStatus(c.status) || canEditStatus(c.status) ? undefined : pageTitleWithName('Data collection', c.name));

  if (isLoading || !c) {
    return (
      <ManagementLayout>
        <LoadingBox loading>
          <div style={{ paddingTop: '20px', minHeight: '200px' }} />
        </LoadingBox>
      </ManagementLayout>
    );
  }

  // Drafts resume the linear "Continue setup" wizard.
  if (isDraftStatus(c.status)) {
    return (
      <ManagementLayout>
        <CollectionWizard
          mode="edit"
          collectionId={c.id}
          initialState={restoredWizardState?.formState ?? hydrateCollectionState(c)}
          initialStep={restoredWizardState?.step}
          allowSaveDraft
        />
      </ManagementLayout>
    );
  }

  // Scheduled / open collections use the section-by-section "change" editor (renders its own layout).
  if (canEditStatus(c.status)) {
    return <CollectionEditor collection={c} />;
  }

  // Closed collections are read-only.
  return (
    <ManagementLayout backLink={{ href: `/admin/collections/${c.id}` }}>
      <div style={{ paddingTop: '20px' }}>
        <H1 className="collections-edit-page__title">{c.name}</H1>
        <GovUKInsetText data-testid="admin-collections-edit-closed-notice">This data collection is closed and can no longer be edited.</GovUKInsetText>
      </div>
    </ManagementLayout>
  );
};

export default CollectionsEdit;
