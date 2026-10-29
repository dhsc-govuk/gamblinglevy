import { AuthError, InteractionRequiredAuthError } from '@azure/msal-browser';

// "The refresh token can no longer be redeemed", in the two shapes MSAL reports it. A token it knows has
// passed its lifetime never reaches the network and is thrown as InteractionRequiredAuthError. One the server
// refuses — revoked, or expired by a rule the browser cannot see — comes back as a plain ServerError carrying
// OAuth's invalid_grant, which is not an InteractionRequiredAuthError unless the response happens to say so.
// The pair is the test MSAL itself applies before falling back to its hidden iframe. A network failure matches
// neither, on purpose: it says nothing about the sign-in.
export const isSignInRequiredError = (error: unknown): boolean =>
  error instanceof InteractionRequiredAuthError || (error instanceof AuthError && error.errorCode === 'invalid_grant');

// Ending the sign-in is components/App's job: it holds the state that stops several requests signing out at
// once. But components/AuthProvider meets the same dead token first, on a page load, and cannot import App
// without a cycle — so App registers how, and both go through here.
let endExpiredSignIn: (() => boolean) | null = null;

export const registerExpiredSignInHandler = (handler: () => boolean): void => {
  endExpiredSignIn = handler;
};

// True when a sign-out is under way and a navigation will follow, so the caller should wait for it rather
// than report a failure of its own.
export const endSignInThatExpired = (): boolean => endExpiredSignIn?.() ?? false;
