namespace Domain.Common;
public static class TimeOnlyExtensions
{
    extension(TimeOnly a)
    {
        public TimeOnly Max(TimeOnly b)
        {
            return a > b ? a : b;
        }

        public TimeOnly Min(TimeOnly b)
        {
            return a < b ? a : b;
        }
    }
}