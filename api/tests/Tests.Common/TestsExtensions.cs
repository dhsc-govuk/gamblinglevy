using LanguageExt;
using Newtonsoft.Json;

namespace Tests.Common;

public static class TestsExtensions
{
    extension<TE, T>(Either<TE, T> result) where TE : Exception
    {
        public T RightOrThrow()
        {
            return result.Match<T>(
                r => r,
                _ => throw new ArgumentException("Value cannot be null"));
        }

        public TE LeftOrThrow()
        {
            return result.Match<TE>(
                _ => throw new ArgumentException("Value cannot be null"),
                e => e);
        }
    }

    public static async Task<T> ToResponseModel<T>(this HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<T>(content)
               ?? throw new ArgumentException("Response content cannot be null.");
    }
}