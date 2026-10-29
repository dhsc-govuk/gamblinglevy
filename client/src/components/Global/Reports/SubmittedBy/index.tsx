import React from 'react';
import { composeTestId } from '../../../../helpers/testIds';

interface SubmittedByValueProps {
  name: string | null;
  email: string | null;
  // The row's testid. The name is `{testId}-name`; the email link keeps `{testId}-action`, the id it had when it was
  // rendered in the summary list's action column, so a selector on the link still finds it. The row's value cell now
  // holds both, so a text assertion meant for the name alone belongs on `{testId}-name`.
  testId: string;
}

// The "Submitted by" value on the report detail pages: the submitter's name, then their email as a mailto link in the
// same cell (BLUM-9018). It used to sit in govuk-summary-list's 20% action column, which an address of ordinary length
// overflows, so the email read as pushed far to the right and its row ran wider than the others. That placement was
// what the original build read from Figma 420-80777; BLUM-9018 moved it next to the name deliberately, on QA's reading
// of the same frame, and the move is the ticket's expected result rather than a drift from the design.
//
// The gap is a real space plus GOV.UK spacing unit 5 on the name — about 30px from tablet up and 20px on mobile. The
// space is what separates the two words in copied text, reader modes and textContent; the margin alone would leave
// "Jane Smithjane@…". It is also the line break: when name and email do not fit together the space is dropped at the
// break and the margin hangs at the end of the first line, so the email starts its own line flush left. An address
// longer than the whole column breaks inside it (the value cell's overflow-wrap).
const SubmittedByValue = ({ name, email, testId }: SubmittedByValueProps): React.ReactElement => (
  <>
    <span className={email ? 'govuk-!-margin-right-5' : undefined} data-testid={composeTestId(testId, 'name')}>
      {name ?? '—'}
    </span>
    {email && (
      <>
        {' '}
        <a className="govuk-link" href={`mailto:${email}`} data-testid={composeTestId(testId, 'action')}>
          {email}
        </a>
      </>
    )}
  </>
);

export default SubmittedByValue;
