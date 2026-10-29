namespace Application.Common.Features;

/// <summary>
/// Every feature flag the service knows. A flag is switched per environment by configuration —
/// <c>Features__{Name}=true</c> as an App Service / DAC environment variable, or the <c>Features</c> block of an
/// appsettings file — and is off wherever it is not set, so new work ships dark to production by default.
///
/// The enum is the registry: a configuration key that matches no member is logged as a warning at boot (a typo'd
/// environment variable otherwise leaves a feature silently off), and deleting a member when the feature is
/// retired turns every remaining use into a compile error. See "Feature Flags" in CLAUDE.md for the migration and
/// data rules.
///
/// A member is named after the Jira ticket that introduces it — <c>BLUM_7637</c> for BLUM-7637 — so a flag in an
/// environment variable, a log line or the client can be traced straight back to its ticket (upper case because
/// SA1300 rejects a lower-case member; names match case-insensitively, so <c>Features__blum_7637</c> works too).
/// The name therefore says nothing about the feature, which is why each member's doc must: what it gates, what
/// data it touches and how to retire it. FeatureTests pins the naming rule.
/// </summary>
public enum Feature
{
    /// <summary>
    /// BLUM-7637 (https://blumhealth.atlassian.net/browse/BLUM-7637): the four strategic feedback questions are optional
    /// and their labels carry " (optional)". Page 1 ("Wider progress and challenges" — SF_OTHER_PREVENTION and
    /// SF_CHALLENGES) came first; BLUM-9092 brought page 2 ("Working together" — SF_DHSC_SUPPORT and SF_LEARNINGS)
    /// under the flag too.
    ///
    /// Data: the four dc_form_questions rows, which SeedStrategicFeedbackModuleAsync reconciles on every boot while
    /// BLUM_9087 is off, so switching the flag converges them in both directions on the next restart. No migration and
    /// no API or client gate — every read of IsRequired and the label is live. While BLUM_9087 is on, DHSC owns the
    /// published questions and this flag reaches only a fresh database, so settle it in every environment before
    /// BLUM_9087 is switched on.
    ///
    /// Retire: hard-code the optional values in the seeder, delete this member and its appsettings key, delete
    /// the flag-off tests in StrategicFeedbackTests.
    /// </summary>
    BLUM_7637,

    /// <summary>
    /// BLUM-8329 (https://blumhealth.atlassian.net/browse/BLUM-8329): strategic-feedback.csv is one row per local
    /// authority with each of the four questions as a column, instead of one row per question under a Section column.
    /// Covers every download of that file — the LA's own report ZIP, DHSC's per-organisation ZIP and DHSC's window
    /// ZIP (where Region, ONS code and E-code move after the four questions).
    ///
    /// Data: none. Both shapes are rendered at download time from the same frozen report snapshot, so there is no
    /// migration, no convergence and no API or client gate.
    ///
    /// Retire: delete the long-format branches in QuarterlyReportExportService and ExportSubmissionsCommand, this
    /// member and its appsettings key, and the flag-off tests in ReportExportSnapshotTests and
    /// DhscSubmissionExportTests.
    /// </summary>
    BLUM_8329,

    /// <summary>
    /// BLUM-8310 (https://blumhealth.atlassian.net/browse/BLUM-8310): "Complete your report offline" on the LA
    /// quarterly report task list. An LA downloads a CSV per section (activity results, funding spent, strategic
    /// feedback), fills it in a spreadsheet and uploads it; a valid file saves and completes that section, an
    /// invalid one is refused with its errors written back into the file. Gates the four
    /// quarterly-report/{collectionId}/offline/* routes (QuarterlyReportOfflineController) and the panel on the
    /// client's Tasks page.
    ///
    /// Data: no new schema. An accepted upload writes the same answer, spend and section rows the web pages write.
    ///
    /// Retire: delete [RequiresFeature] on QuarterlyReportOfflineController and the useFeature check on the Tasks
    /// page, this member and its appsettings key, and the flag-off test in QuarterlyReportOfflineTests.
    /// </summary>
    BLUM_8310,

    /// <summary>
    /// BLUM-9021 (https://blumhealth.atlassian.net/browse/BLUM-9021), the story DHSC approved from spike BLUM-7918: the
    /// DHSC consolidated export's funding-spend.csv closes with one "Grand total" row totalling every money column
    /// across every organisation in the file, below the per-organisation subtotals BLUM-7668 already ships. The LA's
    /// own report ZIP is unaffected.
    ///
    /// Data: none. The row is computed at export time from the frozen snapshots, like the subtotals, so there is no
    /// migration, no convergence and no API or client gate — switching the flag changes the next download only.
    ///
    /// Retire: drop the IsEnabled branch in ExportSubmissionsCommandHandler so the row is always written, delete this
    /// member and its appsettings key, and delete the flag-off half of
    /// DhscSubmissionExportTests.ExportSubmissions_FundingSpend_GrandTotalFlag_AddsOneLastRowAndChangesNothingElse.
    /// Two tests pin the flag OFF and will fail once the row is always there, so they need their assertions changed,
    /// not just the SetGrandTotalAsync(false) line removed:
    /// ExportSubmissions_FundingSpend_ClosesEachOrganisationsBlockWithASubtotalOfItsOwnRows (groups rows by the
    /// Organisation cell — exclude Row type = Grand total first) and
    /// ExportSubmissions_WithRegion_IncludesOnlyThatRegionsOrganisationsInEveryFile (expects every funding-spend.csv
    /// row to start with the organisation's name — skip the grand-total row there).
    /// </summary>
    BLUM_9021,

    /// <summary>
    /// BLUM-9087 (https://blumhealth.atlassian.net/browse/BLUM-9087): form versioning for self-service forms and
    /// questions — one flag for the whole epic. Today it gates BLUM-9102: publishing a form module saves a fixed,
    /// numbered FormModuleVersion (none when the content is unchanged since the last version);
    /// POST admin/data-collection-forms/modules/{id}/draft reopens a published module for editing; and
    /// GET .../modules/{id}/versions[/{n}] and POST .../versions/{n}/restore list, view and restore saved versions;
    /// BLUM-9086: GET .../modules/{id}/versions/collections lists the collections that use each version.
    /// BLUM-9104: a quarterly report reads the activity-delivery and strategic-feedback questions of its collection's
    /// recorded version (CollectionFormModules) for its pages, saves, validation and the copy saved at submit.
    /// BLUM-9105: a release that changes a published form's questions (seeder, convergence or migration) saves a new
    /// version on boot (FormModuleVersionSync); the seeders leave a reopened draft alone, and the strategic-feedback
    /// seeder leaves the module alone once it has been published, because DHSC owns those questions; a published
    /// question code cannot be reused and a published option value cannot change (PublishedQuestionRules).
    /// BLUM-9106: the DHSC window export's summary.csv gains a "Form versions" column, and the collection created,
    /// updated and reverted-to-draft audit rows record each form's version (formVersions, payload_version 2).
    /// BLUM-9103: a collection follows the latest version of each form until its window opens. A publish, saving a
    /// collection, and FormModuleVersionSync on boot move every collection whose window has not opened (and in which no
    /// report has started) onto the latest version, the publish and the sync auditing each as
    /// collection.form_versions_followed. From the open date on a collection's versions are fixed.
    /// BLUM-9084: those moves and the forms lock check that no report has started under the collection's row lock
    /// (CollectionRowLocks), and starting a report reads the collection under the same lock, so a report starting at
    /// the same moment either stops the change or reads what it leaves. Off, no lock is taken.
    /// Spike item 7: show/hide visibility on the server is transitive (AnswerVisibility, transitive: true) — a question
    /// or page whose parent question is hidden is hidden too — in every answer validator, the report and maturity
    /// snapshots, the activity-record export and the offline file. Off, each rule reads its parent's answer alone.
    /// On the client it routes the forms builder (/admin/forms) and shows its links. Off, publishing behaves as before
    /// and writes no version, the endpoints answer 404 and the forms builder has no route.
    ///
    /// Data: form_module_versions, an expand-only table whose rows a trigger keeps immutable, and the version each
    /// collection form records. A publish writes a version only while the flag is on; the boot steps write them either
    /// way (FormModuleVersionBackfill's version 1, FormModuleVersionSync's version of a release's change), and nothing
    /// deletes them when the flag goes off. While the flag is off every report reads the live form, so the sync also
    /// moves each automatically recorded version onto the form's latest version, so that switching the flag on changes
    /// nothing an LA sees. A reopened draft keeps its PublishedAt, which the module seeders read as "already seeded".
    ///
    /// Retire: drop the flag branch in PublishFormModuleCommand, the flag-off branch in FormModuleVersionSync, the
    /// published-module reconcile in SeedStrategicFeedbackModuleAsync (BLUM_7637 must be retired or settled first) and every
    /// [RequiresFeature(Feature.BLUM_9087)] in DataCollectionFormsController, the FeatureRoute and useFeature('BLUM_9087')
    /// checks on the client, delete every transitive parameter and keep AnswerVisibility's transitive mode, take the
    /// collection row locks unconditionally, delete this member, its appsettings key and client union member, and the
    /// flag-off tests in FormModuleVersionTests, ReleaseQuestionChangeTests and MaturityAssessmentAnswersTests.
    /// </summary>
    BLUM_9087,

    /// <summary>
    /// BLUM-9395 (https://blumhealth.atlassian.net/browse/BLUM-9395): routes the activity-record question pages (the
    /// BLUM-6444 form designer) on the client, for a designer to review: /admin/activity-record-questions and its
    /// /create and /{questionId}/edit pages. They were routed for every DHSC admin before this flag. Nothing links to
    /// them either way; they are reached by their address. Off, a DHSC admin opening one gets NotFound.
    ///
    /// The API behind them, FormDesignerController (admin/activity-form), carries [RequiresFeature(Feature.BLUM_9395)]
    /// too, so off it answers a bodiless 404 and the pages render NotFound if the flag goes off while one is open.
    ///
    /// Data: none. The activity-record form itself is unaffected: LAs read it through GET activities/form-schema.
    ///
    /// Retire: to keep the pages, delete the FeatureRoute around the three activityRecordQuestions routes in Routes.tsx
    /// and the [RequiresFeature] on FormDesignerController. To drop them, delete the routes, the FormDesigner views,
    /// FormDesignerController, the authoring commands under Application/Forms/Commands (nothing else sends them) and
    /// FormDesignerTests; keep IFormDesignerControllerService, which ActivitiesController and the activity-record export
    /// also use. Either way, then delete this member, its appsettings key, the client union member and the flag-off test
    /// in FormDesignerTests.
    /// </summary>
    BLUM_9395,
}