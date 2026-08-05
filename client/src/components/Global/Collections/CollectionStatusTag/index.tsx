import React from 'react';
import { GovUKTag } from '../../../GovUKComponents';
import { STATUS_LABEL, STATUS_TAG_COLOUR } from '../../../../views/Admin/Collections/display';
import type { CollectionStatus } from '../../../../views/Admin/Collections/queries';

interface CollectionStatusTagProps {
  status: CollectionStatus;
  testId?: string;
}

// Renders a data-collection status. "Closed" is plain body text (no tag); the others use a coloured GOV.UK tag.
function CollectionStatusTag({ status, testId }: CollectionStatusTagProps): React.ReactElement {
  if (status === 'closed') {
    return (
      <span className="govuk-body" data-testid={testId}>
        {STATUS_LABEL.closed}
      </span>
    );
  }

  return (
    <GovUKTag colour={STATUS_TAG_COLOUR[status]} data-testid={testId}>
      {STATUS_LABEL[status]}
    </GovUKTag>
  );
}

export default CollectionStatusTag;
