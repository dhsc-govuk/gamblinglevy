// Shared date/time rendering for screens outside a feature that already has its own display module.
//
// The same formatter is currently copy-pasted as a local `formatHistoryDate` in half a dozen user and
// organisation views; this is the shared home for it. Existing copies are deliberately left alone —
// consolidating them is not this change's job.

// Rendered in the browser's local time zone, which for this service's users is UK time. The user is being
// asked to recognise their own activity, so a UTC timestamp an hour out during BST would make a genuine
// sign-in look like somebody else's.
// The fallback defaults to "Not recorded" rather than the em dash used elsewhere in the codebase: several
// screen readers skip a bare dash entirely. It stays overridable so the next caller can match whatever its
// own screen says for a missing value instead of inheriting this one's wording.
export const formatDateTime = (iso: string | null | undefined, fallback = 'Not recorded'): string => {
  if (!iso) {
    return fallback;
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  const day = date.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
  const time = date.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });

  return `${day}, ${time}`;
};
