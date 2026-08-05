import React from 'react';
import { ManagementLayout } from '../../../../layouts';
import CollectionWizard from '../../../../components/Global/Collections/CollectionWizard';

// CollectionWizard sets its own step-specific page title (with an "Error: " prefix when relevant), so
// this host page doesn't set a static one.
const CollectionsCreate = (): React.ReactElement => {
  return (
    <ManagementLayout>
      <CollectionWizard mode="create" />
    </ManagementLayout>
  );
};

export default CollectionsCreate;
