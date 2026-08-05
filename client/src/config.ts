import staticConfig from './config.json';

interface AppConfig {
  api_url: string;
  ad_tenant_id: string;
  ad_authority: string;
  ad_client_id: string;
  ad_scope: string;
  ad_redirect_uri: string;
  accessibility_testing_enabled: string;
  sentry_dsn: string;
  app_insights_connection_string: string;
}

const appConfig: AppConfig = {
  api_url: import.meta.env.VITE_API_URL || staticConfig.api_url,
  ad_tenant_id: import.meta.env.VITE_AD_TENANT_ID || staticConfig.ad_tenant_id,
  ad_authority: import.meta.env.VITE_AD_AUTHORITY || staticConfig.ad_authority,
  ad_client_id: import.meta.env.VITE_AD_CLIENT_ID || staticConfig.ad_client_id,
  ad_scope: import.meta.env.VITE_AD_SCOPE || staticConfig.ad_scope,
  ad_redirect_uri: import.meta.env.VITE_AD_REDIRECT_URI || staticConfig.ad_redirect_uri,
  accessibility_testing_enabled: import.meta.env.VITE_ACCESSIBILITY_TESTING_ENABLED || staticConfig.accessibility_testing_enabled,
  sentry_dsn: import.meta.env.VITE_SENTRY_DSN || staticConfig.sentry_dsn,
  app_insights_connection_string: import.meta.env.VITE_APP_INSIGHTS_CONNECTION_STRING || staticConfig.app_insights_connection_string,
};

export default appConfig;
