export const defaultStaleTime = 1000 * 60 * 3;
export const organisationUserStaleTime = 1000 * 60;

export const viewOrganisationCacheKey = (organisationId: string) => `organisations-${organisationId}-view`;
export const viewOrganisationHomeCacheKey = (organisationId: string) => `organisations-${organisationId}-home`;
export const viewLocationCacheKey = (organisationId: string, locationId: string) => `organisations-${organisationId}-locations-${locationId}-view`;
export const viewLocationHomeCacheKey = (organisationId: string, locationId: string) => `organisations-${organisationId}-locations-${locationId}-home`;
export const viewOrganisationUsersCacheKey = (userId: string) => `organisations-users-view-${userId}`;

export const viewLocationUserCacheKey = (organisationId: string, locationId: string, userId: string) => `organisations-${organisationId}-locations-${locationId}-users-${userId}`;

export const viewPatientTestReportCacheKey = (organisationId: string, locationId: string, patientTestReportId: string, params: string) =>
  `organisations-${organisationId}-locations-${locationId}-patient-test-reports-${patientTestReportId}-${params}`;

export const viewPatientTestMetricsCacheKey = (organisationId: string, locationId: string, patientTestReportId: string) =>
  `organisations-${organisationId}-locations-${locationId}-patient-test-reports-${patientTestReportId}-metrics`;

export const viewPatientTestReportSummaryCacheKey = (organisationId: string, locationId: string, patientTestReportId: string) =>
  `organisations-${organisationId}-locations-${locationId}-patient-test-reports-${patientTestReportId}-summary`;

export const profileDateCacheKey = 'profile-data';

// The caller's own active sessions (BLUM-7241). Scoped server-side to the signed-in user, so no id in the
// key — and deliberately no defaultStaleTime at the call site: the user acts on this list to shut somebody
// out, so a cached answer is the one thing it must not give them.
export const mySessionsKey = 'my-sessions';

export const adminUsersListKey = 'admin-users-list';
export const adminUserKey = (userId: string) => ['admin-user', userId] as const;
export const deletedUsersListKey = 'admin-deleted-users-list';
export const deletedUserLogsKey = (userId: string) => ['admin-deleted-user-logs', userId] as const;
export const deletedUserDetailKey = (auditLogId: string) => ['admin-deleted-user-detail', auditLogId] as const;

export const organisationUsersListKey = (organisationId: string) => ['organisation-users-list', organisationId] as const;
export const organisationUserKey = (organisationId: string, userId: string) => ['organisation-user', organisationId, userId] as const;

export const auditLogsListKey = (filterKey: string) => ['audit-logs', filterKey] as const;
export const auditLogKey = (auditLogId: string) => ['audit-log', auditLogId] as const;

export const localAuthoritiesListKey = (onboardingStatus?: string) => ['local-authorities-list', onboardingStatus ?? 'all'] as const;
export const localAuthorityKey = (organisationId: string) => ['local-authority', organisationId] as const;
export const deletedOrganisationsListKey = 'admin-deleted-organisations-list';

// Funding dashboard (BLUM-6213). Scoped server-side to the signed-in user's organisation, so no
// org id is part of the key.
export const laFundingDashboardKey = 'la-funding-dashboard';

export const activitiesListKey = 'activities-list';
export const activitiesDeletedKey = 'activities-deleted';
export const activityKey = (activityId: string) => ['activity', activityId] as const;
export const activityTaxonomyKey = 'activity-taxonomy';

// Activity-record form designer (BLUM-6444). The consumption read (active schema) and the DHSC authoring read
// (full schema) hit different endpoints, so they use separate keys; authoring mutations invalidate both.
export const activityFormSchemaKey = 'activity-form-schema';
export const adminActivityFormSchemaKey = 'admin-activity-form-schema';

// Data collection management (DHSC admin).
export const collectionsListKey = 'data-collections-list';
export const collectionsDeletedKey = 'data-collections-deleted';
export const collectionKey = (id: string) => ['data-collection', id] as const;
export const collectionFormOptionsKey = 'data-collection-form-options';
export const collectionReportingLasKey = 'data-collection-reporting-las';

// DHSC submission visibility (BLUM-6095 / BLUM-6445): the per-window organisation status list and a single
// organisation's submission detail.
export const reportSubmissionsKey = (collectionId: string) => ['report-submissions', collectionId] as const;
export const reportSubmissionDetailKey = (collectionId: string, organisationId: string) => ['report-submission-detail', collectionId, organisationId] as const;

// Forms builder (data-collection form modules, DHSC admin). The list read and the single-module
// read use separate keys; authoring mutations invalidate both as needed.
export const formModulesKey = 'form-modules';
export const formModuleKey = (id: string) => ['form-module', id] as const;

// Quarterly report (BLUM-6611). Every key carries the collection id: an organisation can have two quarterly
// windows open at once, and collection-agnostic keys served one window's task list, activities and answers from
// the cache while the user worked on the other (BLUM-6950).
export const reportContextKey = (collectionId: string) => ['quarterly-report-context', collectionId] as const;
export const reportTaskListKey = (collectionId: string) => ['quarterly-report-task-list', collectionId] as const;
export const reportActivitiesKey = (collectionId: string) => ['quarterly-report-activities', collectionId] as const;
// Prefix of every activity's key-results form in a collection — invalidate this to catch all of them at once when
// the activities themselves change (the forms prefill from the activity record).
export const reportActivityFormsKey = (collectionId: string) => ['quarterly-report-activity-form', collectionId] as const;
export const reportActivityFormKey = (collectionId: string, activityId: string) => [...reportActivityFormsKey(collectionId), activityId] as const;
export const reportFundingSpendKey = (collectionId: string) => ['quarterly-report-funding-spend', collectionId] as const;
export const reportStrategicFeedbackKey = (collectionId: string) => ['quarterly-report-strategic-feedback', collectionId] as const;

// LA submission history + detail (BLUM-6097 / BLUM-6098). The collections list is scoped server-side to the
// signed-in user's organisation, so it carries no org id; the detail key carries the collection id.
export const myReportCollectionsKey = 'la-report-collections';
export const mySubmissionDetailKey = (collectionId: string) => ['la-submission-detail', collectionId] as const;
