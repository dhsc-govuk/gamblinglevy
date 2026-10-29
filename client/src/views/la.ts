// Every view an LA role can reach, as one module, so Routes.tsx can load them with a single dynamic import
// (BLUM-8759). They are one chunk rather than five because they are one surface: the report flow renders the
// activities list and the maturity detail page, and Reports/display reads the other areas' helpers.
export * as OrganisationAdmin from './OrganisationAdmin';
export * as Funding from './Funding';
export * as Activities from './Activities';
export * as Reports from './Reports';
export * as MaturityAssessment from './MaturityAssessment';
