// Cross-cutting constants that are the same in every environment.
//
// FEEDBACK_URL is the DHSC Microsoft Forms feedback form (BLUM-7060). It is a build-time constant rather than
// a config.json key on purpose: BLUM-7060 AC3 requires every environment to reach the *same* form, and
// client/src/config.json is hard-blocked by the husky pre-commit hook, so a per-environment value there could
// not be changed without ceremony for no benefit.
//
// It lives here rather than inside the layout that renders it so BLUM-7632 — which adds a second link to the
// same form elsewhere in the platform — imports this rather than pasting a copy that can drift.
export const FEEDBACK_URL =
  'https://forms.office.com/Pages/ResponsePage.aspx?id=MIwnYaiRMUyMH-9N6Jc6HNz6Q-gd8WZEiJXxWqJKbadUNTk0U1BNTllMSjFMQjFQSlpBMzg4NkRBQy4u&origin=Invitation&channel=0';

// The one wording for the GDS "problem with the service" page, shared with the page title so the tab and the
// heading cannot drift apart. It lives here, not beside the first page that used it, because the page shown when
// a part of the app fails to load (layouts/PageLoadError) is in the entry bundle and must not import
// from components/Global to get a string. UserLoadFailed re-exports it for the pages that already import it there.
export const SERVICE_PROBLEM_HEADING = 'Sorry, there is a problem with the service';

// Contact and reference links used by the audience-specific guidance pages (BLUM-7633). Both the DHSC and
// the LA guidance quote the same support inbox, the same Teams channel and the same gov.uk levy publication,
// so they live here rather than being pasted into each page's sections.tsx — the same reasoning as
// FEEDBACK_URL above. The reporting *deadlines* deliberately stay per-page: the two approved drafts give
// different dates and each page ships its own.
//
// The support inbox is not only the guidance pages': the LA home page, the accessibility statement and the MFA
// "Help with signing in" panel (components/Global/Mfa/SigningInHelp, BLUM-9016) quote it too, so a change here
// reaches the pre-sign-in screens as well. Its response time is quoted by the LA home page and that panel only.
export const GUIDANCE_SUPPORT_EMAIL = 'gamblinglevydata@dhsc.gov.uk';
export const GUIDANCE_SUPPORT_MAILTO = `mailto:${GUIDANCE_SUPPORT_EMAIL}`;
export const GUIDANCE_SUPPORT_RESPONSE_TIME = 'We aim to respond within 5 working days';
export const STATUTORY_GAMBLING_LEVY_URL = 'https://www.gov.uk/government/publications/statutory-gambling-levy';
export const GUIDANCE_TEAMS_CHANNEL_NAME = 'GOV-LA Service - Share progress preventing gambling harms';
export const GUIDANCE_TEAMS_CHANNEL_URL =
  'https://teams.microsoft.com/l/team/19%3AS_fDrMZ3IarHdlGlgFzTbJpf54h716Dygga8yScExlo1%40thread.tacv2/conversations?groupId=8c8df282-0b97-4af2-984f-d9440c4e36f1&tenantId=61278c30-91a8-4c31-8c1f-ef4de8973a1c';
