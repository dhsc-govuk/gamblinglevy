import React from 'react';
import { GovUKTag } from '../../../GovUKComponents';
import { SUBMISSION_STATUS_LABEL, SUBMISSION_STATUS_TAG_COLOUR } from '../../../../views/Admin/Reports/display';
import type { SubmissionStatus } from '../../../../views/Admin/Reports/queries';

interface SubmissionStatusTagProps {
  status: SubmissionStatus;
  // Overrides the DHSC wording. The LA screens pass LA_SUBMISSION_STATUS_LABEL, where a finalised report reads
  // "Submitted" rather than "Completed"; the colour is the same either way.
  label?: string;
  testId?: string;
}

// Renders a derived per-organisation submission status as a coloured GOV.UK tag. The wording defaults to the DHSC
// vocabulary (Not started / In progress / Completed / Overdue); pass `label` for an audience that words it
// differently, as the LA screens do for a finalised report ("Submitted").
function SubmissionStatusTag({ status, label, testId }: SubmissionStatusTagProps): React.ReactElement {
  return (
    <GovUKTag colour={SUBMISSION_STATUS_TAG_COLOUR[status]} data-testid={testId}>
      {label ?? SUBMISSION_STATUS_LABEL[status]}
    </GovUKTag>
  );
}

export default SubmissionStatusTag;
