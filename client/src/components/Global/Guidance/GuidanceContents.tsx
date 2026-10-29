import React, { useId } from 'react';
import { GuidanceSection } from './types';
import { composeTestId } from '../../../helpers/testIds';

interface Props {
  sections: GuidanceSection[];
  testId?: string;
}

// The "Contents" list both approved guidance documents open with (BLUM-7633). It is built from the same
// GuidanceSection[] that renders the body, so an entry can never go missing or point at an id that is not
// on the page, and each item's testid is derived from the same `section.id` as its section's — the
// correspondence is structural rather than a coincidence of two slugs agreeing.
//
// The list is a raw <ul>: `UnorderedList` from GovUKComponents hardcodes `govuk-list--bullet`, and a GDS
// contents list is an unbulleted `govuk-list`, which has no wrapper in this repo.
const GuidanceContents = ({ sections, testId }: Props): React.ReactElement => {
  // useId rather than a literal, so a second contents list on one page cannot duplicate the binding id —
  // the same reason GovUKTable derives its control ids this way.
  const headingId = useId();

  return (
    <nav className="govuk-!-margin-bottom-6" aria-labelledby={headingId} data-testid={testId}>
      <h2 className="govuk-heading-m" id={headingId}>
        Contents
      </h2>
      <ul className="govuk-list">
        {sections.map((section) => (
          <li key={section.id}>
            <a className="govuk-link" href={`#${section.id}`} data-testid={composeTestId(testId, 'item', section.id)}>
              {section.title}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
};

export default GuidanceContents;
