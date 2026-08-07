using Application.Common.Interfaces;

namespace Application.Tests.Unit.Security;

/// <summary>
/// A clock the test moves by hand, so a window's behaviour can be asserted in microseconds instead of by
/// sleeping through it.
///
/// Every fixture here pins it to an instant in the <em>past</em>, and that is not incidental. It is what
/// catches a counter that hands IMemoryCache an absolute expiry derived from this clock: the cache judges
/// absolute expiry against the system clock and silently declines to store an entry that has already
/// expired on arrival, so such a counter stores nothing, reads back zero and admits everything. Pinned to
/// the future, or to now, the same bug passes.
/// </summary>
public sealed class TestClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc);

    public DateTime GetNow() => this.UtcNow;

    public DateTime GetUtcNow() => this.UtcNow;
}