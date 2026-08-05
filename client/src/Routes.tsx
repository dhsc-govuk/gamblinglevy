import { Navigate, Route, Routes, useNavigate } from 'react-router-dom';
import Splash from './views/Public/Splash';
import SignIn from './views/Public/SignIn';
import OrganisationSuspended from './views/Public/OrganisationSuspended';
import Cookies from './views/Public/Cookies';
import { NotAuthorised, NotFound, ProtectedRoleRoute } from './components/GovUKComponents';
import { adminRoles, laRoles, organisationAdminRoles, useAuthProvider } from './components/AuthProvider';
import * as Admin from './views/Admin';
import * as Collections from './views/Admin/Collections';
import * as AdminReports from './views/Admin/Reports';
import * as Forms from './views/Admin/Forms';
import * as OrganisationAdmin from './views/OrganisationAdmin';
import * as Funding from './views/Funding';
import * as Activities from './views/Activities';
import * as Reports from './views/Reports';
import * as Mfa from './views/Mfa';
import * as Account from './views/Account';
import useScrollToTop from './hooks/useScrollToTop';
import { ROUTES } from './helpers/routes';
import LocalAuthoritiesCreateContacts from './views/Admin/Organisations/Create/Contacts';
import LocalAuthoritiesCreateReview from './views/Admin/Organisations/Create/Review';

const AppRoutes = () => {
  const navigate = useNavigate();
  const { userRole, isAuthLoading } = useAuthProvider();

  useScrollToTop();

  return (
    <Routes>
      <Route path={ROUTES.root} element={<Splash />} />
      <Route path={ROUTES.public.signIn} element={<SignIn />} />
      <Route path={ROUTES.public.organisationSuspended} element={<OrganisationSuspended />} />
      <Route path={ROUTES.public.cookies} element={<Cookies />} />

      {/* MFA Routes — accessible after JWT auth, before MFA verification */}
      <Route path={ROUTES.mfa.setup} element={<Mfa.Setup />} />
      <Route path={ROUTES.mfa.verify} element={<Mfa.Verify />} />
      <Route path={ROUTES.mfa.recoveryCodes} element={<Mfa.RecoveryCodes />} />

      <Route element={<ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={adminRoles} userRole={userRole} isLoading={isAuthLoading} />}>
        <Route path={ROUTES.admin.home} element={<Admin.Home />} />
        <Route path={ROUTES.admin.users.list} element={<Admin.UsersList />} />
        <Route path={ROUTES.admin.users.create} element={<Admin.UsersCreate />} />
        <Route path={ROUTES.admin.users.createReview} element={<Admin.UsersCreateReview />} />
        <Route path={ROUTES.admin.users.deleted} element={<Admin.DeletedUsersList />} />
        <Route path={ROUTES.admin.users.deletedDetailPath} element={<Admin.DeletedUsersView />} />
        <Route path={ROUTES.admin.users.deactivatePath} element={<Admin.UsersDeactivate />} />
        <Route path={ROUTES.admin.users.reactivatePath} element={<Admin.UsersReactivate />} />
        <Route path={ROUTES.admin.users.resetMfaPath} element={<Admin.UsersResetMfa />} />
        <Route path={ROUTES.admin.users.deletePath} element={<Admin.UsersDelete />} />
        <Route path={ROUTES.admin.users.detailPath} element={<Admin.UsersView />} />
        <Route path={ROUTES.admin.users.editPath} element={<Admin.UsersEdit />} />
        <Route path={ROUTES.admin.users.editReviewPath} element={<Admin.UsersEditReview />} />
        <Route path={ROUTES.admin.users.editDiscardPath} element={<Admin.UsersEditDiscard />} />

        <Route path={ROUTES.admin.organisations.list} element={<Admin.LocalAuthoritiesList />} />
        <Route path={ROUTES.admin.organisations.create} element={<Admin.LocalAuthoritiesCreate />} />
        <Route path={ROUTES.admin.organisations.createContacts} element={<LocalAuthoritiesCreateContacts />} />
        <Route path={ROUTES.admin.organisations.createReview} element={<LocalAuthoritiesCreateReview />} />
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

        <Route path={ROUTES.admin.activityRecordQuestions.list} element={<Admin.FormQuestionsList />} />
        <Route path={ROUTES.admin.activityRecordQuestions.create} element={<Admin.FormQuestionsCreate />} />
        <Route path={ROUTES.admin.activityRecordQuestions.editPath} element={<Admin.FormQuestionsEdit />} />

        <Route path={ROUTES.admin.forms.list} element={<Forms.FormsModules />} />
        <Route path={ROUTES.admin.forms.createQuestionPath} element={<Forms.FormsQuestionCreate />} />
        <Route path={ROUTES.admin.forms.editQuestionPath} element={<Forms.FormsQuestionEdit />} />
        <Route path={ROUTES.admin.forms.modulePath} element={<Forms.FormsSections />} />

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
      <Route element={<ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={laRoles} userRole={userRole} isLoading={isAuthLoading} />}>
        <Route path={ROUTES.organisationAdmin.home} element={<OrganisationAdmin.Home />} />
        <Route path={ROUTES.organisationAdmin.users.list} element={<OrganisationAdmin.UsersList />} />
        <Route path={ROUTES.organisationAdmin.users.detailPath} element={<OrganisationAdmin.UsersView />} />
      </Route>

      {/* User management — LA Admin only. */}
      <Route
        element={
          <ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={organisationAdminRoles} userRole={userRole} isLoading={isAuthLoading} />
        }>
        <Route path={ROUTES.organisationAdmin.users.create} element={<OrganisationAdmin.UsersCreate />} />
        <Route path={ROUTES.organisationAdmin.users.createReview} element={<OrganisationAdmin.UsersCreateReview />} />
        <Route path={ROUTES.organisationAdmin.users.deleted} element={<OrganisationAdmin.DeletedUsersList />} />
        <Route path={ROUTES.organisationAdmin.users.deletedDetailPath} element={<OrganisationAdmin.DeletedUsersView />} />
        <Route path={ROUTES.organisationAdmin.users.editReviewPath} element={<OrganisationAdmin.UsersEditReview />} />
        <Route path={ROUTES.organisationAdmin.users.editDiscardPath} element={<OrganisationAdmin.UsersEditDiscard />} />
        <Route path={ROUTES.organisationAdmin.users.deactivatePath} element={<OrganisationAdmin.UsersDeactivate />} />
        <Route path={ROUTES.organisationAdmin.users.resetMfaPath} element={<OrganisationAdmin.UsersResetMfa />} />
        <Route path={ROUTES.organisationAdmin.users.deletePath} element={<OrganisationAdmin.UsersDelete />} />
        <Route path={ROUTES.organisationAdmin.users.editPath} element={<OrganisationAdmin.UsersEdit />} />
      </Route>

      {/* Funding dashboard — shared by every LA role (BLUM-6213) */}
      <Route element={<ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={laRoles} userRole={userRole} isLoading={isAuthLoading} />}>
        <Route path={ROUTES.funding.dashboard} element={<Funding.Dashboard />} />
      </Route>

      {/* Activity records — shared by every LA role (BLUM-6031/6032/6034/6035/6036) */}
      <Route element={<ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={laRoles} userRole={userRole} isLoading={isAuthLoading} />}>
        <Route path={ROUTES.activities.list} element={<Activities.ActivitiesList />} />
        <Route path={ROUTES.activities.create} element={<Activities.ActivitiesCreate />} />
        <Route path={ROUTES.activities.deleted} element={<Activities.ActivitiesDeleted />} />
        <Route path={ROUTES.activities.detailPath} element={<Activities.ActivitiesView />} />
        <Route path={ROUTES.activities.editPath} element={<Activities.ActivitiesEdit />} />
        <Route path={ROUTES.activities.deletePath} element={<Activities.ActivitiesDelete />} />
        <Route path={ROUTES.activities.restorePath} element={<Activities.ActivitiesRestore />} />
      </Route>

      {/* Quarterly report submission — shared by every LA role (BLUM-6611) */}
      <Route element={<ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={laRoles} userRole={userRole} isLoading={isAuthLoading} />}>
        {/* LA Reports area: landing + submission history + detail (BLUM-6097 / BLUM-6098) */}
        <Route path={ROUTES.reports.list} element={<Reports.ReportsLanding />} />
        <Route path={ROUTES.reports.submitted} element={<Reports.PreviousReports />} />
        <Route path={ROUTES.reports.submittedDetailPath} element={<Reports.SubmissionDetail />} />
        {/* The quarterly authoring flow is collection-scoped (BLUM-6950) — the bare entry can no longer say which
            collection to open, so it redirects to the landing to pick one. */}
        <Route path={ROUTES.reports.quarterlyLegacy} element={<Navigate to={ROUTES.reports.list} replace />} />
        <Route path={ROUTES.reports.quarterlyPath} element={<Reports.Intro />} />
        <Route path={ROUTES.reports.tasksPath} element={<Reports.Tasks />} />
        <Route path={ROUTES.reports.confirmActivitiesPath} element={<Reports.ConfirmActivities />} />
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
        {/* Deep bookmarks from the pre-BLUM-6950 unparameterised flow (…/funding-spent/forecast, …/key-results/{id}/check)
            match none of the scoped routes above — send them to the landing rather than the generic 404 page. Declared
            last for readability; react-router ranks by specificity, so the splat never shadows a scoped route. */}
        <Route path={ROUTES.reports.quarterlyLegacyWildcard} element={<Navigate to={ROUTES.reports.list} replace />} />
      </Route>

      {/* The signed-in user's own account (BLUM-7241). Open to every role — an admin's session needs ending
          just as much as an LA lead's — so the gate here is "is signed in at all", spelled as all three roles
          because there is no any-authenticated-role constant. */}
      <Route
        element={
          <ProtectedRoleRoute fallbackComponent={<NotAuthorised goBack={navigate} />} allowedRoles={[...adminRoles, ...laRoles]} userRole={userRole} isLoading={isAuthLoading} />
        }>
        <Route path={ROUTES.account.sessions} element={<Account.Sessions />} />
      </Route>

      <Route path={ROUTES.notFound} element={<NotFound goBack={navigate} />} />
    </Routes>
  );
};

export default AppRoutes;
