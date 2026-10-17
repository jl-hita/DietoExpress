using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class UserTimeZoneResolverTests
{
    [Fact]
    public void TryConvertLocalToUtc_SkipsLocalTimesThatDoNotExistAtSpringTransition()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        var local = new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified);

        var converted = UserTimeZoneResolver.TryConvertLocalToUtc(local, zone, out _);

        Assert.False(converted);
    }

    [Fact]
    public void TryConvertLocalToUtc_ChoosesFirstOccurrenceForAmbiguousAutumnTime()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        var local = new DateTime(2026, 10, 25, 2, 30, 0, DateTimeKind.Unspecified);

        var converted = UserTimeZoneResolver.TryConvertLocalToUtc(local, zone, out var utc);

        Assert.True(converted);
        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), utc);
    }
}
