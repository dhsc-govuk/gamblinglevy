// Every view only a DHSC admin can reach, as one module, so Routes.tsx can load them with a single dynamic
// import and an LA user's pages do not wait on them (BLUM-8759). An LA user who opens an admin URL still
// downloads this before being refused — ProtectedRoleRoute starts the download while the role is unknown.
export * as Admin from './Admin';
export * as Collections from './Admin/Collections';
export * as AdminReports from './Admin/Reports';
