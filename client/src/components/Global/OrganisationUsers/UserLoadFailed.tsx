import React, { useEffect, useRef } from 'react';
import { H1, Paragraph } from '../../GovUKComponents';

// The one wording for this page, shared with the page title so the tab and the heading cannot drift apart.
export const SERVICE_PROBLEM_HEADING = 'Sorry, there is a problem with the service';

export interface UserLoadFailedProps {
  testId: string;
}

// The honest answer when a user record cannot be read for a reason that is NOT a refusal — a 5xx, a dropped
// connection, a profile with no organisation. The denial page says "you do not have permission", which would be a
// false statement about someone's access rights during an outage (BLUM-7113).
//
// It carries its own <h1> and is rendered as a page's entire content, never beside another heading: the branch it
// replaces owns the heading, so without one these pages had zero level-1 headings while the tab still advertised the
// action the user could no longer take. Unlike NotAuthorised it renders inside the layout, so the back link and
// navigation survive and there is always a way forward.
//
// It moves focus rather than announcing through a live region, matching NotAuthorised and NotFound: all three are the
// same event — an async response replacing the page — and one mechanism is one thing to verify. Focus also relocates
// the user, which role="alert" does not: it would announce the failure and still leave a keyboard user tabbing from
// the top of the layout.
// The ref sits on the wrapper, not the heading: the shared H1 is a plain function component, and making it a
// forwardRef to serve this one page would change a primitive used across the whole service.
const UserLoadFailed = ({ testId }: UserLoadFailedProps): React.ReactElement => {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    containerRef.current?.focus();
  }, []);

  return (
    <div ref={containerRef} tabIndex={-1} data-testid={testId}>
      <H1>{SERVICE_PROBLEM_HEADING}</H1>
      <Paragraph>Could not load this user&apos;s details.</Paragraph>
      <Paragraph>Try again later.</Paragraph>
    </div>
  );
};

export default UserLoadFailed;
