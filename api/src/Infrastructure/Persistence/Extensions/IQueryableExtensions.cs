using System.Linq.Expressions;

namespace Infrastructure.Persistence.Extensions;

public static class IQueryableExtensions
{
    extension<T>(IQueryable<T> source)
    {
        public IQueryable<T> OrderByDynamic(string orderByProperty)
        {
            return source.OrderBy(ToLambda<T>(orderByProperty));
        }

        public IQueryable<T> OrderByDescendingDynamic(string orderByProperty)
        {
            return source.OrderByDescending(ToLambda<T>(orderByProperty));
        }
    }

    private static Expression<Func<T, object>> ToLambda<T>(string propertyName)
    {
        var param = Expression.Parameter(typeof(T), "x");
        Expression body = Expression.PropertyOrField(param, propertyName);

        if (body.Type.IsValueType)
        {
            body = Expression.Convert(body, typeof(object));
        }

        return Expression.Lambda<Func<T, object>>(body, param);
    }
}