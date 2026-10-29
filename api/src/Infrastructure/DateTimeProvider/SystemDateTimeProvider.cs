using Application.Common.Interfaces;

namespace Infrastructure.DateTimeProvider;
public class SystemDateTimeProvider : IDateTimeProvider
{
    // The one place the system clock is read. Everything else takes it from here, or as a utcNow parameter.
#pragma warning disable RS0030
    public DateTime GetUtcNow() => DateTime.UtcNow;
#pragma warning restore RS0030
}