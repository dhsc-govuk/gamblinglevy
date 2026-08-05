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

const root = ReactDOM.createRoot(document.getElementById('root') as HTMLElement);
root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);

reportAccessibility(React);
