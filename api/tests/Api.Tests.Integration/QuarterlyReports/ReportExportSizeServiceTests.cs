using Api.Services.Abstract;
using Api.Services.Implementation;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// BLUM-9018: the detail screens caption their download with its byte length. The integration tests prove the size
// matches the downloaded file; these pin what no fixture can reach — that a measurement which throws reads as "size
// unknown" instead of failing the detail read (the BLUM-7216 defect measuring used to cause), and that a report is
// built once per cache window however often its detail screen is read (the reads sit on the Read rate-limit budget,
// not the Export one).
//
// Lives in this project because the service is in the Api layer, which Application.Tests.Unit does not reference. It
// declares no class fixture, so it boots no host and creates no database.
public class ReportExportSizeServiceTests
{
    private readonly ReportExportSizeService _service =
        new(new MemoryCache(new MemoryCacheOptions()), NullLogger<ReportExportSizeService>.Instance);

    private static ReportExportSizeKey NewKey(string surface = "la") =>
        new(surface, Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 8, 26, 10, 4, 0, DateTimeKind.Utc));

    private static Func<Task<Option<ReportExportFile>>> Counting(Func<Option<ReportExportFile>> result, Action onBuild) =>
        () =>
        {
            onBuild();
            return Task.FromResult(result());
        };

    [Fact]
    public async Task ShouldBeTheBuiltFilesByteLength()
    {
        var size = await _service.MeasureAsync(
            NewKey(),
            () => Task.FromResult(Option<ReportExportFile>.Some(new ReportExportFile("report.zip", new byte[398_336]))));

        size.Should().Be(398_336);
    }

    [Fact]
    public async Task ShouldBeNull_WhenThereIsNoFile()
    {
        var size = await _service.MeasureAsync(NewKey(), () => Task.FromResult(Option<ReportExportFile>.None));

        size.Should().BeNull();
    }

    [Fact]
    public async Task ShouldBeNull_WhenBuildingTheFileThrows()
    {
        var size = await _service.MeasureAsync(
            NewKey(),
            () => throw new InvalidOperationException("The strategic feedback file was built for a snapshot with no strategic feedback."));

        size.Should().BeNull();
    }

    [Fact]
    public async Task ShouldLetACancellationThrough()
    {
        // A caller who dropped the request is not a broken export: swallowing it would carry on reading for nobody.
        var measure = () => _service.MeasureAsync(NewKey(), () => throw new OperationCanceledException());

        await measure.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ShouldBuildAReportOnce_HoweverOftenItIsMeasured()
    {
        var key = NewKey();
        var builds = 0;
        var build = Counting(() => new ReportExportFile("report.zip", new byte[1_536]), () => builds++);

        var first = await _service.MeasureAsync(key, build);
        var second = await _service.MeasureAsync(key with { }, build);

        first.Should().Be(1_536);
        second.Should().Be(1_536);
        builds.Should().Be(1);
    }

    [Fact]
    public async Task ShouldRememberAFailure_SoABrokenExportIsNotRebuiltOnEveryView()
    {
        var key = NewKey();
        var builds = 0;
        Func<Task<Option<ReportExportFile>>> build = () =>
        {
            builds++;
            throw new InvalidOperationException("broken export");
        };

        (await _service.MeasureAsync(key, build)).Should().BeNull();
        (await _service.MeasureAsync(key, build)).Should().BeNull();

        builds.Should().Be(1);
    }

    [Fact]
    public async Task ShouldMeasureEachSurfaceAndReportOnItsOwn()
    {
        // The LA's call and DHSC's can build different files for one report, and two reports never share a size.
        var key = NewKey("la");
        var builds = 0;
        var build = Counting(() => new ReportExportFile("report.zip", new byte[10]), () => builds++);

        await _service.MeasureAsync(key, build);
        await _service.MeasureAsync(key with { Surface = "dhsc" }, build);
        await _service.MeasureAsync(key with { OrganisationId = Guid.NewGuid() }, build);
        await _service.MeasureAsync(key with { SubmittedAt = key.SubmittedAt.AddMinutes(1) }, build);

        builds.Should().Be(4);
    }
}