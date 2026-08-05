import React from 'react';
import { GovUKTag, type TagColour } from '../../../GovUKComponents';
import type { ActivityStatus } from '../../../../views/Activities/queries';
import { statusLabel } from '../../../../views/Activities/display';

const STATUS_COLOUR: Record<ActivityStatus, TagColour> = {
  planned: 'blue',
  active: 'green',
  closed: 'grey',
  finished: 'grey',
};

interface Props {
  status: ActivityStatus | null;
  isDraft?: boolean;
  testId?: string;
}

const ActivityStatusTag = ({ status, isDraft = false, testId }: Props): React.ReactElement => (
  <GovUKTag colour={isDraft || !status ? 'blue' : STATUS_COLOUR[status]} data-testid={testId}>
    {statusLabel(status, isDraft)}
  </GovUKTag>
);

export default ActivityStatusTag;
