import React from 'react';
import { GovUKSummaryCard, SummaryList } from '../../../GovUKComponents';
import { composeTestId, slugifyTestId } from '../../../../helpers/testIds';
import { readOnlyPageRows } from '../../../../views/MaturityAssessment/display';
import type { MaturitySnapshotSectionDto } from '../../../../views/MaturityAssessment/queries';

interface ReadOnlySectionCardsProps {
  section: MaturitySnapshotSectionDto;
  // The cards' heading level: 2 under a page H1, 4 when the section itself is an h3 on a page listing them all.
  headingLevel?: 2 | 3 | 4;
  testId: string;
}

// One submitted section as read-only summary cards, one per page, straight from the frozen snapshot (BLUM-7859).
// No actions — a submitted assessment cannot be changed. Shared by the LA's own view and the DHSC detail.
const ReadOnlySectionCards = ({ section, headingLevel = 2, testId }: ReadOnlySectionCardsProps): React.ReactElement => (
  <div data-testid={testId}>
    {section.pages.map((page) => (
      <GovUKSummaryCard key={page.code} title={page.title} headingLevel={headingLevel} testId={composeTestId(testId, slugifyTestId(page.code))}>
        <SummaryList items={readOnlyPageRows(page)} halfWidthColumns plainSubAnswers testId={composeTestId(testId, slugifyTestId(page.code), 'list')} />
      </GovUKSummaryCard>
    ))}
  </div>
);

export default ReadOnlySectionCards;
