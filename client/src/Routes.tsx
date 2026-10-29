import { Suspense } from 'react';
import { Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import Splash from './views/Public/Splash';
import SignIn from './views/Public/SignIn';
import OrganisationSuspended from './views/Public/OrganisationSuspended';
import AccountDeactivated from './views/Public/AccountDeactivated';
import Cookies from './views/Public/Cookies';
import AccessibilityStatement from './views/Public/AccessibilityStatement';
import MfaVerify from './views/Mfa/Verify';
import * as Account from './views/Account';
import { ChunkErrorBoundary, FeatureRoute, NotAuthorised, NotFound, PageLoading, ProtectedRoleRoute } from './components/GovUKComponents';
import { adminRoles, laRoles, organisationAdminRoles, useAuthProvider } from './components/AuthProvider';
import PageLoadError from './layouts/PageLoadError';
import useScrollToTop from './hooks/useScrollToTop';
import { forgetFailedViews, lazyChunk } from './helpers/lazyViews';
import { ROUTES } from './helpers/routes';

// Route-level code splitting (BLUM-8759). The views above are in the entry bundle: everything on the way to a
// signed-in home page (splash, sign-in, the MFA code screen), the pages a failure lands on, and the two policy
// pages. Everything else arrives in one of three chunks, by who can reach it — see helpers/lazyViews for why
// the grain is the audience and not the route.
//
// Add a view to its area's barrel and route it as <Area.View />; it is then lazy with nothing else to do. Do
// not import a page from views/ statically in this file: that puts its whole area back in the entry bundle,
// and the entry-chunk guard in vite.config.mts fails the build when it happens.
const dhsc = lazyChunk(() => import('./views/dhsc'));
const la = lazyChunk(() => import('./views/la'));
// First-time enrolment only. Verify is in the same barrel but is imported statically above, because every
// sign-in passes through it; listing the two names keeps <Mfa.Verify /> a type error here.
const mfa = lazyChunk(() => import('./views/Mfa'));

const Admin = dhsc.views((views) => views.Admin);
const Collections = dhsc.views((views) => views.Collections);
const AdminReports = dhsc.views((views) => views.AdminReports);
const OrganisationAdmin = la.views((views) => views.OrganisationAdmin);
const Funding = la.views((views) => views.Funding);
const Activities = la.views((views) => views.Activities);
const Reports = la.views((views) => views.Reports);
const MaturityAssessment = la.views((views) => views.MaturityAssessment);
const Mfa = mfa.views(({ Setup, RecoveryCodes }) => ({ Setup, RecoveryCodes }));

const RouteTable = () => {
  const navigate = useNavigate();
  const { userRole, isAuthLoading, features } = useAuthProvider();
  // For the one protected group whose views are not lazy: a page load that lands on the account page would
  // otherwise leave the first click into the rest of the app waiting on a chunk nothing had asked for yet.
  const preloadOwnViews = userRole ? (adminRoles.includes(userRole) ? dhsc.preload : la.preload) : undefined;

  return (
    <Routes>
      <Route path={ROUTES.root} element={<Splash />} />
      <Route path={ROUTES.public.signIn} element={<SignIn />} />
      <Route path={ROUTES.public.organisationSuspended} element={<OrganisationSuspended />} />
      <Route path={ROUTES.public.accountDeactivated} element={<AccountDeactivated />} />
      <Route path={ROUTES.public.cookies} element={<Cookies />} />
      <Route path={ROUTES.public.accessibilityStatement} element={<AccessibilityStatement />} />

      {/* MFA Routes — accessible after JWT auth, before MFA verification */}
      <Route path={ROUTES.mfa.setup} element={<Mfa.Setup />} />
      <Route path={ROUTES.mfa.verify} element={<MfaVerify />} />
      <Route path={ROUTES.mfa.recoveryCodes} element={<Mfa.RecoveryCodes />} />

      <Route
        element={
          <ProtectedRoleRoute
            fallbackComponent={<NotAuthorised goBack={navigate} />}
            allowedRoles={adminRoles}
            userRole={userRole}
            isLoading={isAuthLoading}
            preload={dhsc.preload}
          />
        }>
        <Route path={ROUTES.admin.home} element={<Admin.Home />} />
        <Route path={ROUTES.admin.users.list} element={<Admin.UsersList />} />
        <Route path={ROUTES.admin.users.create} element={<Admin.UsersCreate />} />
        <Route path={ROUTES.admin.users.createReview} element={<Admin.UsersCreateReview />} />
        <Route path={ROUTES.admin.users.deleted} element={<Admin.DeletedUsersList />} />
        <Route path={ROUTES.admin.users.deletedDetailPath} element={<Admin.DeletedUsersView />} />
        <Route path={ROUTES.admin.users.deactivatePath} element={<Admin.UsersDeactivate />} />
        <Route path={ROUTES.admin.users.reactivatePath} element={<Admin.UsersReactivate />} />
        <Route path={ROUTES.admin.users.resetMfaPath} element={<Admin.UsersResetMfa />} />
        <Route path={ROUTES.admin.users.enablePasswordLoginPath} element={<Admin.UsersEnablePasswordLogin />} />
        <Route path={ROUTES.admin.users.resetPasswordPath} element={<Admin.UsersResetPassword />} />
        <Route path={ROUTES.admin.users.deletePath} element={<Admin.UsersDelete />} />
        <Route path={ROUTES.admin.users.detailPath} element={<Admin.UsersView />} />
        <Route path={ROUTES.admin.users.editPath} element={<Admin.UsersEdit />} />
        <Route path={ROUTES.admin.users.editReviewPath} element={<Admin.UsersEditReview />} />
        <Route path={ROUTES.admin.users.editDiscardPath} element={<Admin.UsersEditDiscard />} />

        <Route path={ROUTES.admin.organisations.list} element={<Admin.LocalAuthoritiesList />} />
        <Route path={ROUTES.admin.organisations.create} element={<Admin.LocalAuthoritiesCreate />} />
        <Route path={ROUTES.admin.organisations.createContacts} element={<Admin.LocalAuthoritiesCreateContacts />} />
        <Route path={ROUTES.admin.organisations.createReview} element={<Admin.LocalAuthoritiesCreateReview />} />
        <Route path={ROUTES.admin.organisations.bulkImport} element={<Admin.LocalAuthoritiesBulkImport />} />
        <Route path={ROUTES.admin.organisations.bulkAllocations} element={<Admin.LocalAuthoritiesBulkAmendAllocations />} />
        <Route path={ROUTES.admin.organisations.deleted} element={<Admin.DeletedOrganisationsList />} />
        <Route path={ROUTES.admin.organisations.detailPath} element={<Admin.LocalAuthoritiesView />} />
        <Route path={ROUTES.admin.organisations.suspendPath} element={<Admin.LocalAuthoritiesSuspend />} />
        <Route path={ROUTES.admin.organisations.reinstatePath} element={<Admin.LocalAuthoritiesReinstate />} />
        <Route path={ROUTES.admin.organisations.deletePath} element={<Admin.LocalAuthoritiesDelete />} />
        <Route path={ROUTES.admin.organisations.editPath} element={<Admin.LocalAuthoritiesEdit />} />
        <Route path={ROUTES.admin.organisations.editContactsPath} element={<Admin.LocalAuthoritiesEditContacts />} />
        <Route path={ROUTES.admin.organisations.editReviewPath} element={<Admin.LocalAuthoritiesEditReview />} />
        <Route path={ROUTES.admin.organisations.editDiscardPath} element={<Admin.LocalAuthoritiesEditDiscard />} />
        <Route path={ROUTES.admin.organisations.amendAllocationPath} element={<Admin.LocalAuthoritiesAmendAllocation />} />
        <Route path={ROUTES.admin.organisations.usersPath} element={<Admin.LocalAuthorityUsersList />} />
        <Route path={ROUTES.admin.organisations.createUserPath} element={<Admin.LocalAuthorityUsersCreate />} />
        <Route path={ROUTES.admin.organisations.userPath} element={<Admin.LocalAuthorityUsersView />} />
        <Route path={ROUTES.admin.organisations.editUserPath} element={<Admin.LocalAuthorityUsersEdit />} />

        <Route path={ROUTES.admin.auditLogs.list} element={<Admin.AuditLogList />} />
        <Route path={ROUTES.admin.auditLogs.detailPath} element={<Admin.AuditLogView />} />

        {/* DHSC guidance (BLUM-7633). The Management side nav and the Management header tab's activePrefixes
            pointed at this path since before the page existed, so until BLUM-7633 the link 404'd. BLUM-7819
            then moved the entry point off Management entirely: it is now a top-level "Guidance" tab in the
            header service navigation, matching the LA roles. */}
        <Route path={ROUTES.admin.guidance} element={<Admin.Guidance />} />

        {/* The activity-record question pages (BLUM-6444). Routed for every DHSC admin until BLUM-9395; now only while
            BLUM_9395 is on, for a designer to review them. Nothing links to them, on purpose, with the flag on or off.
            Off, a DHSC admin gets NotFound here; other roles get this group's NotAuthorised. */}
        <Route element={<FeatureRoute feature="BLUM_9395" features={features} fallbackComponent={<NotFound goBack={navigate} />} />}>
          <Route path={ROUTES.admin.activityRecordQuestions.list} element={<Admin.FormQuestionsList />} />
          <Route path={ROUTES.admin.activityRecordQuestions.create} element={<Admin.FormQuestionsCreate />} />
          <Route path={ROUTES.admin.activityRecordQuestions.editPath} element={<Admin.FormQuestionsEdit />} />
        </Route>

        {/* The data-collection forms builder, back behind BLUM_9087 (form versioning, BLUM-9087). BLUM-7464 hid it
            and BLUM-8756 deleted it; it returns only with versioning, so a publish saves a fixed version and a
            published form is reopened as a draft rather than edited in place. Off, a DHSC admin gets NotFound here,
            the same as the API's 404 for the draft endpoint; other roles get this group's NotAuthorised. */}
        <Route element={<FeatureRoute feature="BLUM_9087" features={features} fallbackComponent={<NotFound goBack={navigate} />} />}>
          <Route path={ROUTES.admin.forms.list} element={<Admin.FormsModules />} />
          <Route path={ROUTES.admin.forms.createQuestionPath} element={<Admin.FormsQuestionCreate />} />
          <Route path={ROUTES.admin.forms.editQuestionPath} element={<Admin.FormsQuestionEdit />} />
          <Route path={ROUTES.admin.forms.modulePath} element={<Admin.FormsSections />} />
          <Route path={ROUTES.admin.forms.versionsPath} element={<Admin.FormsVersions />} />
          <Route path={ROUTES.admin.forms.versionPath} element={<Admin.FormsVersion />} />
        </Route>

        <Route path={ROUTES.admin.reports.list} element={<AdminReports.ReportsList />} />
        <Route path={ROUTES.admin.reports.collectionPath} element={<AdminReports.ReportOrganisations />} />
        <Route path={ROUTES.admin.reports.organisationPath} element={<AdminReports.ReportSubmissionDetail />} />

        <Route path={ROUTES.admin.collections.list} element={<Collections.CollectionsList />} />
        <Route path={ROUTES.admin.collections.create} element={<Collections.CollectionsCreate />} />
        <Route path={ROUTES.admin.collections.deleted} element={<Collections.CollectionsDeleted />} />
        <Route path={ROUTES.admin.collections.detailPath} element={<Collections.CollectionsView />} />
        <Route path={ROUTES.admin.collections.editDiscardPath} element={<Collections.CollectionsDiscard />} />
        <Route path={ROUTES.admin.collections.editPath} element={<Collections.CollectionsEdit />} />
        <Route path={ROUTES.admin.collections.deletePath} element={<Collections.CollectionsDelete />} />
        <Route path={ROUTES.admin.collections.restorePath} element={<Collections.CollectionsRestore />} />
        <Route path={ROUTES.admin.collections.revertPath} element={<Collections.CollectionsRevert />} />
        <Route path={ROUTES.admin.collections.closePath} element={<Collections.CollectionsClose />} />
        <Route path={ROUTES.admin.collections.sendReminderPath} element={<Collections.CollectionsReminder />} />
      </Route>

      {/* LA account surface shared by every LA role: the home page plus READ-ONLY access to the LA's user
          list and detail. An LA Lead sees who its colleagues are but cannot manage them (BLUM-7038 AC3) —
          the mutating routes sit in the LA-Admin-only group below, and the affordances that lead to them
          are hidden via canManageUsers. */}
      <Route
        element={
          <ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={laRoles} userRole={userRole} isLoading={isAuthLoading} preload={la.preload} />
        }>
        <Route path={ROUTES.organisationAdmin.home} element={<OrganisationAdmin.Home />} />
        <Route path={ROUTES.organisationAdmin.users.list} element={<OrganisationAdmin.UsersList />} />
        <Route path={ROUTES.organisationAdmin.users.detailPath} element={<OrganisationAdmin.UsersView />} />
        {/* LA guidance (BLUM-7633) — laRoles, not organisationAdminRoles: reading the guidance is not user
            management, and the "Guidance" header tab is rendered for an LA Lead too. */}
        <Route path={ROUTES.organisationAdmin.guidance} element={<OrganisationAdmin.Guidance />} />
      </Route>

      {/* LA Admin-only surfaces: the planned trends destination and user management. */}
      <Route
        element={
          <ProtectedRoleRoute
            fallbackComponent={<NotAuthorised goBack={navigate} />}
            allowedRoles={organisationAdminRoles}
            userRole={userRole}
            isLoading={isAuthLoading}
            preload={la.preload}
          />
        }>
        <Route path={ROUTES.organisationAdmin.trends} element={<OrganisationAdmin.UpcomingFeature title="View data trends" message="Data trends are not available yet." />} />
        <Route path={ROUTES.organisationAdmin.users.create} element={<OrganisationAdmin.UsersCreate />} />
        <Route path={ROUTES.organisationAdmin.users.createReview} element={<OrganisationAdmin.UsersCreateReview />} />
        <Route path={ROUTES.organisationAdmin.users.deleted} element={<OrganisationAdmin.DeletedUsersList />} />
        <Route path={ROUTES.organisationAdmin.users.deletedDetailPath} element={<OrganisationAdmin.DeletedUsersView />} />
        <Route path={ROUTES.organisationAdmin.users.editReviewPath} element={<OrganisationAdmin.UsersEditReview />} />
        <Route path={ROUTES.organisationAdmin.users.editDiscardPath} element={<OrganisationAdmin.UsersEditDiscard />} />
        <Route path={ROUTES.organisationAdmin.users.deactivatePath} element={<OrganisationAdmin.UsersDeactivate />} />
        <Route path={ROUTES.organisationAdmin.users.resetMfaPath} element={<OrganisationAdmin.UsersResetMfa />} />
        <Route path={ROUTES.organisationAdmin.users.enablePasswordLoginPath} element={<OrganisationAdmin.UsersEnablePasswordLogin />} />
        <Route path={ROUTES.organisationAdmin.users.resetPasswordPath} element={<OrganisationAdmin.UsersResetPassword />} />
        <Route path={ROUTES.organisationAdmin.users.deletePath} element={<OrganisationAdmin.UsersDelete />} />
        <Route path={ROUTES.organisationAdmin.users.editPath} element={<OrganisationAdmin.UsersEdit />} />
      </Route>

      {/* Funding dashboard — shared by every LA role (BLUM-6213) */}
      <Route
        element={
          <ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={laRoles} userRole={userRole} isLoading={isAuthLoading} preload={la.preload} />
        }>
        <Route path={ROUTES.funding.dashboard} element={<Funding.Dashboard />} />
      </Route>

      {/* Activity records — shared by every LA role (BLUM-6031/6032/6034/6035/6036) */}
      <Route
        element={
          <ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={laRoles} userRole={userRole} isLoading={isAuthLoading} preload={la.preload} />
        }>
        <Route path={ROUTES.activities.list} element={<Activities.ActivitiesList />} />
        <Route path={ROUTES.activities.create} element={<Activities.ActivitiesCreate />} />
        <Route path={ROUTES.activities.deleted} element={<Activities.ActivitiesDeleted />} />
        <Route path={ROUTES.activities.detailPath} element={<Activities.ActivitiesView />} />
        <Route path={ROUTES.activities.editPath} element={<Activities.ActivitiesEdit />} />
        <Route path={ROUTES.activities.deletePath} element={<Activities.ActivitiesDelete />} />
        <Route path={ROUTES.activities.restorePath} element={<Activities.ActivitiesRestore />} />
      </Route>

      {/* Quarterly report submission — shared by every LA role (BLUM-6611) */}
      <Route
        element={
          <ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={laRoles} userRole={userRole} isLoading={isAuthLoading} preload={la.preload} />
        }>
        {/* LA Reports area: landing + submission history + detail (BLUM-6097 / BLUM-6098) */}
        <Route path={ROUTES.reports.list} element={<Reports.ReportsLanding />} />
        <Route path={ROUTES.reports.submitted} element={<Reports.PreviousReports />} />
        <Route path={ROUTES.reports.submittedDetailPath} element={<Reports.SubmissionDetail />} />
        {/* The quarterly authoring flow is collection-scoped (BLUM-6950) — the bare entry can no longer say which
            collection to open, so it redirects to the landing to pick one. */}
        <Route path={ROUTES.reports.quarterlyLegacy} element={<Navigate to={ROUTES.reports.list} replace />} />
        <Route path={ROUTES.reports.quarterlyPath} element={<Reports.Tasks />} />
        <Route path={ROUTES.reports.tasksPath} element={<Reports.Tasks />} />
        <Route path={ROUTES.reports.confirmActivitiesPath} element={<Reports.ConfirmActivities />} />
        {/* Pre-BLUM-7563 bookmarks of these pages used a /key-results segment; redirect them to the renamed
            path rather than letting the landing-page fallback below swallow the deep link. */}
        <Route path={ROUTES.reports.keyResultsLegacyWildcard} element={<Reports.KeyResultsLegacyRedirect />} />
        <Route path={ROUTES.reports.keyResultsPath} element={<Reports.KeyResultsSelect />} />
        <Route path={ROUTES.reports.keyResultsActivityPath} element={<Reports.KeyResultsResults />} />
        <Route path={ROUTES.reports.keyResultsDeliveryPath} element={<Reports.KeyResultsDelivery />} />
        <Route path={ROUTES.reports.keyResultsCheckPath} element={<Reports.KeyResultsCheck />} />
        <Route path={ROUTES.reports.fundingSpentPath} element={<Reports.FundingSpentSpending />} />
        <Route path={ROUTES.reports.fundingSpentForecastPath} element={<Reports.FundingSpentForecast />} />
        <Route path={ROUTES.reports.fundingSpentCheckPath} element={<Reports.FundingSpentCheck />} />
        <Route path={ROUTES.reports.strategicFeedbackPath} element={<Reports.StrategicFeedbackProgress />} />
        <Route path={ROUTES.reports.strategicFeedbackWorkingTogetherPath} element={<Reports.StrategicFeedbackWorkingTogether />} />
        <Route path={ROUTES.reports.strategicFeedbackCheckPath} element={<Reports.StrategicFeedbackCheck />} />
        <Route path={ROUTES.reports.submitPath} element={<Reports.Submit />} />
        <Route path={ROUTES.reports.submittedConfirmationPath} element={<Reports.Submitted />} />
        {/* Annual maturity assessment (BLUM-7855 / 7858 / 7859), shared by every LA role like the quarterly report.
            Declared static-first for readability; react-router ranks the static submit/submitted/check segments above
            the :sectionCode/:pageCode params regardless of order. */}
        <Route path={ROUTES.reports.maturityPath} element={<MaturityAssessment.MaturityTasks />} />
        <Route path={ROUTES.reports.maturitySubmitPath} element={<MaturityAssessment.MaturitySubmit />} />
        <Route path={ROUTES.reports.maturitySubmittedConfirmationPath} element={<MaturityAssessment.MaturitySubmitted />} />
        <Route path={ROUTES.reports.maturitySectionCheckPath} element={<MaturityAssessment.MaturitySectionCheck />} />
        <Route path={ROUTES.reports.maturitySectionPath} element={<MaturityAssessment.MaturitySectionEntry />} />
        <Route path={ROUTES.reports.maturitySectionPagePath} element={<MaturityAssessment.MaturitySectionPage />} />
        <Route path={ROUTES.reports.submittedMaturitySectionPath} element={<MaturityAssessment.MaturitySubmittedSection />} />
        {/* Deep bookmarks from the pre-BLUM-6950 unparameterised flow (…/funding-spent/forecast, …/results/{id}/check)
            match none of the scoped routes above — send them to the landing rather than the generic 404 page. Declared
            last for readability; react-router ranks by specificity, so the splat never shadows a scoped route. */}
        <Route path={ROUTES.reports.quarterlyLegacyWildcard} element={<Navigate to={ROUTES.reports.list} replace />} />
      </Route>

      {/* The signed-in user's own account (BLUM-7241). Open to every role — an admin's session needs ending
          just as much as an LA lead's — so the gate here is "is signed in at all", spelled as all three roles
          because there is no any-authenticated-role constant. */}
      <Route
        element={
          <ProtectedRoleRoute
            fallbackComponent={<NotAuthorised goBack={navigate} />}
            allowedRoles={[...adminRoles, ...laRoles]}
            userRole={userRole}
            isLoading={isAuthLoading}
            preload={preloadOwnViews}
          />
        }>
        <Route path={ROUTES.account.sessions} element={<Account.Sessions />} />
      </Route>

      <Route path={ROUTES.notFound} element={<NotFound goBack={navigate} />} />
    </Routes>
  );
};

// One boundary for the whole table: nothing above <Routes> draws a header or footer (each view renders its own
// layout), so there is no chrome for a narrower boundary to keep on screen. The fallback has no delay — it takes
// over from ProtectedRoleRoute's loader, and a gap between the two would show as a blank page.
//
// The table is its own component, inside the boundary, on purpose: Admin, Reports and the rest are read while
// RouteTable renders, which after an error is after onReset has discarded the views that failed. Read out here
// instead, they would be the failed components again and navigating could never recover.
const AppRoutes = () => {
  const location = useLocation();

  useScrollToTop();

  return (
    <ChunkErrorBoundary resetKey={location.key} onReset={forgetFailedViews} fallback={<PageLoadError />}>
      <Suspense fallback={<PageLoading testId="route-chunk-loading" />}>
        <RouteTable />
      </Suspense>
    </ChunkErrorBoundary>
  );
};

export default AppRoutes;
