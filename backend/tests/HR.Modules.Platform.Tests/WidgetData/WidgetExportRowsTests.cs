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
        public int LastPageSize;

        public Task<WidgetDataResult> GetRowsAsync(WidgetQuerySpec spec, string? segmentKey,
            IReadOnlyList<WidgetFilterSpec>? df, int page, int pageSize, CancellationToken ct)
        {
            LastSpec = spec;
            LastSegment = segmentKey;
            LastPageSize = pageSize;
            return Task.FromResult(new WidgetDataResult
            {
                Kind = "table",
                Columns = new() { new TableColumn { Code = "name", Label = "Name" } },
                Rows = new() { new Dictionary<string, object?> { ["name"] = "Ali" } },
                TotalCount = 1,
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
    public async Task ExportRows_runs_GetRows_flattens_and_writes()
    {
        var data = new FakeWidgetData();
        var writer = new FakeWriter();
        var sut = new WidgetExportService(data, new IExportWriter[] { writer }, db: null!);

        var file = await sut.ExportRowsAsync(Spec(), segmentKey: "3", dashboardFilters: null,
            format: ExportFormat.Excel, title: "Employees", ct: default);

        data.LastSegment.Should().Be("3");
        data.LastPageSize.Should().Be(5000);            // MaxExportRows
        writer.Written.Should().NotBeNull();
        file.Content.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        file.ContentType.Should().Be("application/xlsx");
        file.FileName.Should().Contain("Employees").And.EndWith(".xlsx");
    }

    [Fact]
    public async Task ExportRows_unsupported_format_throws()
    {
        var sut = new WidgetExportService(new FakeWidgetData(), Array.Empty<IExportWriter>(), db: null!);
        await FluentActions.Invoking(() => sut.ExportRowsAsync(Spec(), null, null, ExportFormat.Pdf, "t", default))
            .Should().ThrowAsync<HR.Application.Common.Exceptions.ValidationException>();
    }
}
