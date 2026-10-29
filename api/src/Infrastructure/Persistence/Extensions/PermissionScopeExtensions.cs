using System.Linq.Expressions;
using Application.Common.Permissions;
using Domain.Organisations;
using Domain.ValueObjects;
using LanguageExt.UnsafeValueAccess;

namespace Infrastructure.Persistence.Extensions;

public static class PermissionScopeExtensions
{
    extension<T>(IQueryable<T> source)
    {
        /// <summary>
        /// Narrows a query to what the caller's permission may see — the one place a role becomes an
        /// organisation scope. DHSC admin sees every row; an organisation member sees the rows
        /// <paramref name="inOrganisation"/> places in their organisation.
        /// </summary>
        /// <remarks>
        /// Any other permission throws <see cref="PermissionScopeException"/> here, at the call, rather than
        /// when the query runs, so the failure names the role instead of surfacing as an EF error about an
        /// in-memory source. Adding a role means deciding its scope here; <c>PermissionScopeTests</c> fails
        /// until it is.
        /// </remarks>
        public IQueryable<T> ScopedTo(Permission permission, Func<OrganisationId, Expression<Func<T, bool>>> inOrganisation)
        {
            var role = permission.UserRole;

            if (role.Equals(UserRole.Admin))
            {
                return source;
            }

            if (!role.IsOrganisationMember())
            {
                throw PermissionScopeException.UnscopedRole(role);
            }

            if (permission.OrganisationId.IsNone)
            {
                throw PermissionScopeException.MissingOrganisation(role);
            }

            return source.Where(inOrganisation(permission.OrganisationId.ValueUnsafe()));
        }
    }
}