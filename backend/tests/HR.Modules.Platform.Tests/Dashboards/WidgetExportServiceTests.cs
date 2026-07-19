using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.WidgetData;
using Xunit;

namespace HR.Modules.Platform.Tests.Dashboards;

/// <summary>
/// DB-free tests for <see cref="WidgetExportService"/>.
/// The format-guard runs before any DB call, so these tests need no seeded widget.
/// A full end-to-end (seed DashboardWidget → export Excel) requires REPORTS_TEST_DB
/// and is deferred; flattener coverage in <see cref="WidgetResultFlattenerTests"/> is
/// the required pure-logic gate for this feature.
/// </summary>
public class WidgetExportServiceTests
{
    [Fact]
    public async Task Unknown_format_throws_ValidationException_before_db_hit()
    {
        // Arrange: no writers registered → every format is "unsupported"
        var svc = new WidgetExportService(new NeverCalledWidgetDataService(), Array.Empty<IExportWriter>(), null!);

        // Act
        Func<Task> act = () => svc.ExportAsync(Guid.NewGuid(), (ExportFormat)99, CancellationToken.None);

        // Assert: guard short-circuits before any DB/data call.
        // ValidationException always has message "One or more validation errors occurred."
        // — check the Errors dictionary instead.
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("format");
    }

    /// <summary>Stub that fails loudly if any method is called — proves the format guard short-circuits.</summary>
    private sealed class NeverCalledWidgetDataService : IWidgetDataService
    {
        public Task<WidgetDataResult> ExecuteAsync(WidgetQuerySpec spec, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, CancellationToken ct)
            => throw new InvalidOperationException("IWidgetDataService must NOT be called when the format is invalid.");

        public Task<WidgetDataResult> ExecuteWidgetAsync(Guid widgetId, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, CancellationToken ct)
            => throw new InvalidOperationException("IWidgetDataService must NOT be called when the format is invalid.");

        public Task<WidgetDataResult> GetRowsAsync(WidgetQuerySpec spec, string? segmentKey, IReadOnlyList<WidgetFilterSpec>? dashboardFilters, int page, int pageSize, CancellationToken ct)
            => throw new InvalidOperationException("IWidgetDataService must NOT be called when the format is invalid.");
    }
}
