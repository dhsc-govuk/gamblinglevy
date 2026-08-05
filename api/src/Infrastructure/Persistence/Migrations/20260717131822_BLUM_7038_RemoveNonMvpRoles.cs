using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_7038_RemoveNonMvpRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BLUM-7038 reduces the role set to admin / organisation admin / lead. users.role is materialised
            // through UserRole.From, which throws UnsupportedUserRoleException on an unknown string, so any row
            // left holding a removed role crash-loops boot the moment it is read. Collapse them here, before the
            // seeders run (MigrateAsync precedes SeedDataAsync in Program.cs).
            //
            // Location admins are re-homed onto their location's organisation rather than deleted: as leads they
            // resolve their profile through organisation_users, so without that row they would authenticate and
            // then receive no claims at all.
            //
            // organisation_users.id is set to user_id, NOT a synthetic md5(...)::uuid as elsewhere in this
            // folder: every OrganisationUsers command reads one route guid as BOTH the OrganisationUserId and
            // the UserId, and UserDto surfaces the org-user id back to the client as the user id. id == user_id
            // is a system invariant; a synthetic id would leave every migrated user un-fetchable and un-editable.
            //
            // The DELETE is unconditional, NOT gated on role = 'location admin': a location_users row can
            // outlive that role. AdminEditUserCommand re-homes a location admin to 'organisation admin' and
            // adds an organisation_users row but never clears the join table, so a role-gated DELETE would
            // leave that stale row behind — and because this change removes every surface that creates
            // location_users rows, nothing downstream could ever clear it. That orphan then bricks user
            // deletion: DeleteOrganisationUserCommand HardDeletes the CIAM identity first, then RemoveAsync
            // trips the RESTRICT fk_location_users_users_user_id and 500s forever. Draining the table here,
            // now that it is write-dead, is the safe end state.
            //
            // Order is load-bearing: (1) the INSERT reads location_users to re-home the pure location admins
            // (role = 'location admin', still no organisation_users row), (2) the DELETE then empties the
            // table, and (3) the UPDATE destroys the role = 'location admin' predicate the INSERT depended on.
            // Each statement is individually idempotent. Note the spaces in the role strings ('read only').
            migrationBuilder.Sql(
                """
                INSERT INTO organisation_users (id, user_id, organisation_id)
                SELECT
                    lu.user_id,
                    lu.user_id,
                    l.organisation_id
                FROM location_users AS lu
                INNER JOIN locations AS l ON l.id = lu.location_id
                INNER JOIN users AS u ON u.id = lu.user_id
                WHERE u.role = 'location admin'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM organisation_users AS existing
                      WHERE existing.user_id = lu.user_id
                  );

                DELETE FROM location_users;

                UPDATE users
                SET role = 'lead'
                WHERE role IN ('location admin', 'contributor', 'finance', 'read only');
                """);

            // Repair rows that already violate the id == user_id invariant. AdminEditUserCommand minted a random
            // OrganisationUserId.New() when re-homing a user (fixed in the same change), which left those users
            // permanently 404ing on the LA users API. Safe because organisation_users.user_id is unique, so the
            // rewritten id cannot collide with another row.
            migrationBuilder.Sql(
                """
                UPDATE organisation_users
                SET id = user_id
                WHERE id <> user_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reference-data cleanup is not reversible.
        }
    }
}