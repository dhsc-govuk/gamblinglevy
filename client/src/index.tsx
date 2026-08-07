import React from 'react';
import ReactDOM from 'react-dom/client';
import * as Sentry from '@sentry/react';
import App from './components/App';

import appConfig from './config.json';
import reportAccessibility from './report-accessibility';
import './helpers/analytics';

if (appConfig.sentry_dsn !== '') {
  Sentry.init({
    dsn: appConfig.sentry_dsn,
    integrations: [Sentry.browserTracingIntegration()],
    tracesSampleRate: 1.0,
    environment: import.meta.env.MODE,
    release: 'gambling-levy-core-app@1.0.0',
    beforeSend: (event) => {
      const status = event.contexts?.response?.status_code;
      const errorCode = (event.extra as { errorCode?: string } | undefined)?.errorCode;
      if (status === 403 && (errorCode === 'MFA_REQUIRED' || errorCode === 'MFA_SETUP_REQUIRED' || errorCode === 'ORGANISATION_SUSPENDED')) {
        return null;
      }
      return event;
    },
  });
}

// govuk-frontend scopes its JavaScript-dependent rules to this class, including the ones that hide an
// unselected conditional reveal (.govuk-radios__conditional--hidden / .govuk-checkboxes__conditional--hidden).
// Without it those panels are always visible (BLUM-7040). Set before React's first render so none flashes open.
//
// React — not govuk-frontend's JS — drives those reveals; this app loads govuk's CSS only, never initAll.
// So the class also means any govuk component that needs JS renders in its JS-enabled state with nothing
// to drive it: .govuk-accordion__section-content and .govuk-tabs__panel--hidden would be display:none and
// never open. Nothing renders that markup today. If you add an accordion or tabs, drive it from React or
// initialise govuk-frontend's JS module.
//
// This lived in an inline <script> in index.html until BLUM-7237: an inline script forces 'unsafe-inline'
// into script-src, which is exactly what the Content-Security-Policy hardening had to remove.
document.body.classList.add('govuk-frontend-supported');

const root = ReactDOM.createRoot(document.getElementById('root') as HTMLElement);
root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);

reportAccessibility(React);
