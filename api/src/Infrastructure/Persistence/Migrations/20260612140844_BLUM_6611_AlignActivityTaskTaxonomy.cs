using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_6611_AlignActivityTaskTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BLUM-6611 aligns the activity-type taxonomy to the Question Protocol so the key-result protocol seed
            // (which resolves its question blocks by task name) attaches to every type. Already-seeded environments
            // hold the pre-alignment lists; this migration converges them by name. Every statement is guarded so it
            // no-ops on fresh databases (which seed the aligned lists directly) and on re-runs.

            // --- Strategy and leadership -------------------------------------------------------------------------
            // Rename: 'Documenting a formal strategy and action plan' -> 'Documenting a strategy and action plan'.
            migrationBuilder.Sql("""
                UPDATE activity_tasks SET task_name = 'Documenting a strategy and action plan', updated_at = now()
                WHERE task_name = 'Documenting a formal strategy and action plan'
                  AND NOT EXISTS (
                    SELECT 1 FROM activity_tasks t2
                    WHERE t2.task_name = 'Documenting a strategy and action plan' AND t2.category_id = activity_tasks.category_id);
                """);

            // Merge the two steering-group types into the protocol's single type: rename the first, re-point any
            // activities referencing the second, then remove the second.
            migrationBuilder.Sql("""
                UPDATE activity_tasks SET task_name = 'Setting up and running a local steering group', updated_at = now()
                WHERE task_name = 'Setting up a local steering group'
                  AND NOT EXISTS (
                    SELECT 1 FROM activity_tasks t2
                    WHERE t2.task_name = 'Setting up and running a local steering group' AND t2.category_id = activity_tasks.category_id);
                """);
            migrationBuilder.Sql("""
                UPDATE activities SET task_id = (
                    SELECT id FROM activity_tasks WHERE task_name = 'Setting up and running a local steering group' LIMIT 1)
                WHERE task_id IN (SELECT id FROM activity_tasks WHERE task_name = 'Running a local steering group')
                  AND EXISTS (SELECT 1 FROM activity_tasks WHERE task_name = 'Setting up and running a local steering group');
                """);
            migrationBuilder.Sql("""
                DELETE FROM activity_tasks WHERE task_name = 'Running a local steering group'
                  AND NOT EXISTS (SELECT 1 FROM activities a WHERE a.task_id = activity_tasks.id);
                """);

            // Merge the two regional-network types into the protocol's single type.
            migrationBuilder.Sql("""
                UPDATE activity_tasks SET task_name = 'Joining and attending a regional network', updated_at = now()
                WHERE task_name = 'Setting up or joining a regional network'
                  AND NOT EXISTS (
                    SELECT 1 FROM activity_tasks t2
                    WHERE t2.task_name = 'Joining and attending a regional network' AND t2.category_id = activity_tasks.category_id);
                """);
            migrationBuilder.Sql("""
                UPDATE activities SET task_id = (
                    SELECT id FROM activity_tasks WHERE task_name = 'Joining and attending a regional network' LIMIT 1)
                WHERE task_id IN (SELECT id FROM activity_tasks WHERE task_name = 'Attending or running a regional network')
                  AND EXISTS (SELECT 1 FROM activity_tasks WHERE task_name = 'Joining and attending a regional network');
                """);
            migrationBuilder.Sql("""
                DELETE FROM activity_tasks WHERE task_name = 'Attending or running a regional network'
                  AND NOT EXISTS (SELECT 1 FROM activities a WHERE a.task_id = activity_tasks.id);
                """);

            // Final ordering for the four protocol strategy types.
            migrationBuilder.Sql("""
                UPDATE activity_tasks SET sequence_order = o.seq
                FROM (VALUES
                    ('Documenting a strategy and action plan', 1),
                    ('Setting up and running a local steering group', 2),
                    ('Joining and attending a regional network', 3),
                    ('Other type of strategy or leadership', 4)) AS o(name, seq)
                WHERE activity_tasks.task_name = o.name
                  AND activity_tasks.category_id IN (SELECT id FROM categories WHERE name = 'Strategy and leadership');
                """);

            // --- Licensing, planning and policy ------------------------------------------------------------------
            // Rename: '...local-authority owned spaces' -> '...council-owned spaces' (protocol wording).
            migrationBuilder.Sql("""
                UPDATE activity_tasks SET task_name = 'Restricting gambling advertising on council-owned spaces', updated_at = now()
                WHERE task_name = 'Restricting gambling advertising on local-authority owned spaces'
                  AND NOT EXISTS (
                    SELECT 1 FROM activity_tasks t2
                    WHERE t2.task_name = 'Restricting gambling advertising on council-owned spaces' AND t2.category_id = activity_tasks.category_id);
                """);

            // Insert the protocol type missing from the original seed.
            migrationBuilder.Sql("""
                INSERT INTO activity_tasks (id, category_id, task_name, description, sequence_order, is_active, created_by, created_at)
                SELECT gen_random_uuid(), c.id, 'Responding to gambling licence applications', NULL, 2, TRUE, u.id, now()
                FROM categories c
                CROSS JOIN (SELECT id FROM users WHERE role = 'admin' LIMIT 1) u
                WHERE c.name = 'Licensing, planning and policy'
                  AND NOT EXISTS (
                    SELECT 1 FROM activity_tasks t WHERE t.task_name = 'Responding to gambling licence applications' AND t.category_id = c.id);
                """);

            // 'Developing policy to stop gambling venues opening' is not a protocol type: deactivate it (existing
            // activities referencing it keep working with custom-only key results).
            migrationBuilder.Sql("""
                UPDATE activity_tasks SET is_active = FALSE, updated_at = now()
                WHERE task_name = 'Developing policy to stop gambling venues opening';
                """);

            migrationBuilder.Sql("""
                UPDATE activity_tasks SET sequence_order = o.seq
                FROM (VALUES
                    ('Reviewing and evolving licensing policy', 1),
                    ('Responding to gambling licence applications', 2),
                    ('Assessing the risks of existing and planned gambling venues', 3),
                    ('Managing planning applications for gambling venues', 4),
                    ('Restricting gambling advertising on council-owned spaces', 5),
                    ('Other type of licensing, planning or policy', 6)) AS o(name, seq)
                WHERE activity_tasks.task_name = o.name
                  AND activity_tasks.category_id IN (SELECT id FROM categories WHERE name = 'Licensing, planning and policy');
                """);

            // --- Education, campaigns and outreach ---------------------------------------------------------------
            // Rename to the protocol's training type (Education category only — the Help copy is handled below).
            migrationBuilder.Sql("""
                UPDATE activity_tasks SET task_name = 'Training your workforce on preventing gambling harms', updated_at = now()
                WHERE task_name = 'Training staff to spot signs of harm and refer people to support'
                  AND category_id IN (SELECT id FROM categories WHERE name = 'Education, campaigns and outreach')
                  AND NOT EXISTS (
                    SELECT 1 FROM activity_tasks t2
                    WHERE t2.task_name = 'Training your workforce on preventing gambling harms' AND t2.category_id = activity_tasks.category_id);
                """);

            // --- Help, advice and access to services -------------------------------------------------------------
            // The original seed copied Education's list into Help; the protocol defines distinct Help types.
            // Insert the protocol types, then remove the unreferenced copies (referenced ones are only deactivated).
            migrationBuilder.Sql("""
                INSERT INTO activity_tasks (id, category_id, task_name, description, sequence_order, is_active, created_by, created_at)
                SELECT gen_random_uuid(), c.id, n.name, NULL, n.seq, TRUE, u.id, now()
                FROM categories c
                CROSS JOIN (SELECT id FROM users WHERE role = 'admin' LIMIT 1) u
                CROSS JOIN (VALUES
                    ('Improving referrals between services', 1),
                    ('Using screening tools to better assess gambling harm', 2),
                    ('Working with organisations to support people affected by gambling harms', 3),
                    ('Other type of help, advice or support activity', 4)) AS n(name, seq)
                WHERE c.name = 'Help, advice and access to services'
                  AND NOT EXISTS (SELECT 1 FROM activity_tasks t WHERE t.task_name = n.name AND t.category_id = c.id);
                """);
            migrationBuilder.Sql("""
                DELETE FROM activity_tasks
                WHERE category_id IN (SELECT id FROM categories WHERE name = 'Help, advice and access to services')
                  AND task_name IN (
                    'Training staff to spot signs of harm and refer people to support',
                    'Briefing elected members on gambling-related harms',
                    'Running campaigns and events to raise awareness among local residents and at-risk groups',
                    'Creating a workplace charter',
                    'Promoting support services to specific communities',
                    'Involving people with lived experience in planning and peer support',
                    'Other type of education, campaign or outreach')
                  AND NOT EXISTS (SELECT 1 FROM activities a WHERE a.task_id = activity_tasks.id);
                """);
            migrationBuilder.Sql("""
                UPDATE activity_tasks SET is_active = FALSE, updated_at = now()
                WHERE category_id IN (SELECT id FROM categories WHERE name = 'Help, advice and access to services')
                  AND task_name IN (
                    'Training staff to spot signs of harm and refer people to support',
                    'Briefing elected members on gambling-related harms',
                    'Running campaigns and events to raise awareness among local residents and at-risk groups',
                    'Creating a workplace charter',
                    'Promoting support services to specific communities',
                    'Involving people with lived experience in planning and peer support',
                    'Other type of education, campaign or outreach');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reference-data cleanup is not reversible.
        }
    }
}