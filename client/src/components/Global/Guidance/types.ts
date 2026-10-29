import React from 'react';

// One top-level (H2) section of a guidance page. `id` is the in-page anchor the contents list links to, so
// it has to be unique within the page and stable — it is a shareable URL fragment, not a rendering detail.
// GuidancePage resolves an incoming fragment on mount, so a link to one of these ids lands on the section
// rather than the top of the page; renaming an id silently breaks any link already shared.
export interface GuidanceSection {
  id: string;
  title: string;
  content: React.ReactNode;
}
