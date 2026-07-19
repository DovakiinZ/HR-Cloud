using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.WidgetData;
using Xunit;

namespace HR.Modules.Platform.Tests.WidgetData;

public class WidgetExportRowsTests
{
    private sealed class FakeWidgetData : IWidgetDataService
    {
        public WidgetQuerySpec? LastSpec;
        public string? LastSegment;
        public List<int> RequestedPages { get; } = new();
        public List<int> RequestedPageSizes { get; } = new();

        // Simulate: TotalCount=350, page 1 returns 200 rows, page 2 returns 150 rows, page 3+ returns 0
        private const int SimulatedTotal = 350;
        private const int PageCap = 200;

        public Task<WidgetDataResult> GetRowsAsync(WidgetQuerySpec spec, string? segmentKey,
            IReadOnlyList<WidgetFilterSpec>? df, int page, int pageSize, CancellationToken ct)
        {
            LastSpec = spec;
            LastSegment = segmentKey;
            RequestedPages.Add(page);
            RequestedPageSizes.Add(pageSize);

            int offset = (page - 1) * PageCap;
            int remaining = Math.Max(0, SimulatedTotal - offset);
            int count = Math.Min(remaining, PageCap);

            var rows = new List<Dictionary<string, object?>>();
            for (int i = 0; i < count; i++)
                rows.Add(new Dictionary<string, object?> { ["name"] = $"Row-{offset + i}" });

            return Task.FromResult(new WidgetDataResult
            {
                Kind = "table",
                Columns = new() { new TableColumn { Code = "name", Label = "Name" } },
                Rows = rows,
                TotalCount = SimulatedTotal,
            });
        }

        public Task<WidgetDataResult> ExecuteAsync(WidgetQuerySpec s, IReadOnlyList<WidgetFilterSpec>? d, CancellationToken c)
            => throw new NotImplementedException();

        public Task<WidgetDataResult> ExecuteWidgetAsync(Guid id, IReadOnlyList<WidgetFilterSpec>? d, CancellationToken c)
            => throw new NotImplementedException();
    }

    private sealed class FakeWidgetDataLarge : IWidgetDataService
    {
        // Simulate TotalCount > 5000 to verify MaxExportRows cap
        private const int SimulatedTotal = 6000;
        private const int PageCap = 200;
        public List<int> RequestedPages { get; } = new();

        public Task<WidgetDataResult> GetRowsAsync(WidgetQuerySpec spec, string? segmentKey,
            IReadOnlyList<WidgetFilterSpec>? df, int page, int pageSize, CancellationToken ct)
        {
            RequestedPages.Add(page);
            int offset = (page - 1) * PageCap;
            int remaining = Math.Max(0, SimulatedTotal - offset);
            int count = Math.Min(remaining, PageCap);

            var rows = new List<Dictionary<string, object?>>();
            for (int i = 0; i < count; i++)
                rows.Add(new Dictionary<string, object?> { ["id"] = offset + i });

            return Task.FromResult(new WidgetDataResult
            {
                Kind = "table",
                Columns = new() { new TableColumn { Code = "id", Label = "ID" } },
                Rows = rows,
                TotalCount = SimulatedTotal,
            });
        }

        public Task<WidgetDataResult> ExecuteAsync(WidgetQuerySpec s, IReadOnlyList<WidgetFilterSpec>? d, CancellationToken c)
            => throw new NotImplementedException();

        public Task<WidgetDataResult> ExecuteWidgetAsync(Guid id, IReadOnlyList<WidgetFilterSpec>? d, CancellationToken c)
            => throw new NotImplementedException();
    }

    private sealed class FakeWriter : IExportWriter
    {
        public ExportFormat Format => ExportFormat.Excel;
        public string ContentType => "application/xlsx";
        public string Extension => "xlsx";
        public TabularDataset? Written;

        public byte[] Write(TabularDataset data, ExportWriteOptions? options = null)
        {
            Written = data;
            return new byte[] { 1, 2, 3 };
        }
    }

    private static WidgetQuerySpec Spec() => new() { ObjectCode = "Employee", Aggregation = "Count" };

    [Fact]
    public async Task ExportRows_paginates_GetRows_and_accumulates_all_rows()
    {
        var data = new FakeWidgetData();
        var writer = new FakeWriter();
        var sut = new WidgetExportService(data, new IExportWriter[] { writer }, db: null!);

        var file = await sut.ExportRowsAsync(Spec(), segmentKey: "3", dashboardFilters: null,
            format: ExportFormat.Excel, title: "Employees", ct: default);

        // Should have called GetRowsAsync with pageSize=200 (matching engine clamp), not 5000
        data.RequestedPageSizes.Should().AllSatisfy(ps => ps.Should().Be(200));

        // Should have fetched page 1 and page 2
        data.RequestedPages.Should().Contain(1).And.Contain(2);

        // Written dataset should contain all 350 rows
        writer.Written.Should().NotBeNull();
        writer.Written!.Rows.Should().HaveCount(350);

        data.LastSegment.Should().Be("3");
        file.Content.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        file.ContentType.Should().Be("application/xlsx");
        file.FileName.Should().Contain("Employees").And.EndWith(".xlsx");
    }

    [Fact]
    public async Task ExportRows_caps_at_MaxExportRows_5000_when_totalCount_exceeds_limit()
    {
        var data = new FakeWidgetDataLarge();
        var writer = new FakeWriter();
        var sut = new WidgetExportService(data, new IExportWriter[] { writer }, db: null!);

        await sut.ExportRowsAsync(Spec(), segmentKey: null, dashboardFilters: null,
            format: ExportFormat.Excel, title: "BigExport", ct: default);

        // Should be capped at exactly MaxExportRows = 5000
        writer.Written!.Rows.Should().HaveCount(5000);

        // Should have fetched exactly 25 pages (5000 / 200) before stopping
        data.RequestedPages.Should().HaveCount(25);
    }

    [Fact]
    public async Task ExportRows_unsupported_format_throws()
    {
        var sut = new WidgetExportService(new FakeWidgetData(), Array.Empty<IExportWriter>(), db: null!);
        await FluentActions.Invoking(() => sut.ExportRowsAsync(Spec(), null, null, ExportFormat.Pdf, "t", default))
            .Should().ThrowAsync<HR.Application.Common.Exceptions.ValidationException>();
    }
}
